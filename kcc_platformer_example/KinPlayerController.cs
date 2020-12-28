using System;
using UnityEngine;

namespace Reflaection
{
	public enum CharacterState
	{
		Default,
		Dying,
		Revive,
		Pause
	}

	public struct PlayerCharacterInputs
	{
		public float MoveAxisForward;
		public bool JumpDown;
		public bool CrouchDown;
		public bool CrouchUp;
		public bool InteractDown;
		public bool EscapeDown;
		public bool FireDown;
	}

	public class KinPlayerController : MonoBehaviour, ICharacterController
	{
		[SerializeField] private KinematicCharacterMotor Motor;

		[Header("Stable Movement")]
		[SerializeField] private float MaxStableMoveSpeed = 3.0f;
		[SerializeField] private float StableMovementSharpness = 15f;
		[SerializeField] private float MaxCrouchMoveSpeed = 2.0f;

		[Header("Air Movement")]
		[SerializeField] private float MaxAirMoveSpeed = 2.5f;
		[SerializeField] private float AirAccelerationSpeed = 75.0f;
		[SerializeField] private float Drag = 0.33f;

		[Header("Jumping")]
		[SerializeField] private float JumpUpSpeed = 7.33f;
		[SerializeField] private float JumpScalableForwardSpeed = 1.0f;
		[SerializeField] private float JumpPreGroundingGraceTime = -0.1f;
		[SerializeField] private float JumpPostGroundingGraceTime = -0.1f;

		[Header("Animation Parameters")]
		[SerializeField] private Animator CharacterAnimator;

		[Header("Misc")]
		public Vector3 Gravity = new Vector3(0, -12.5f, 0);

		[Header("Sounds")]
		[SerializeField] private AudioClip moveSound;
		[SerializeField] private AudioClip interactSound;
		[SerializeField] private AudioClip shootSound;
		[SerializeField] private AudioClip dieSound;
		[SerializeField] private AudioClip landSound;
		[SerializeField] private AudioClip bumpSound;
		[SerializeField] private AudioClip breakSound;
		[SerializeField] private AudioClip fireSound;

		public CharacterState CurrentCharacterState { get; private set; }

		// Event handlers
		public delegate void UpdateHUDLivesAction(int lifeIncrement);
		public static event UpdateHUDLivesAction OnUpdateHUDLives;

		public delegate void ResetLevelAction();
		public static event ResetLevelAction OnResetLevel;

		public delegate void ChangeUIAction(int uiNumber);
		public static event ChangeUIAction OnChangeUI;

		private Collider[] _probedColliders = new Collider[8];
		private Vector3 _moveInputVector;

		// Handle jumping
		private bool _jumpRequested = false;
		private bool _jumpConsumed = false;
		private bool _jumpedThisFrame = false;

		// Used to determine how high player jumped/fell
		private float _timeSinceJumpRequested = Mathf.Infinity;
		private float _timeSinceLastAbleToJump = 0f;
		private Vector3 _internalVelocityAdd = Vector3.zero;

		// Handle interaction
		private bool _interactRequested = false;
		private bool _interactConsumed = false;
		private bool _interactedThisFrame = false;
		private float _timeSinceInteractRequested = Mathf.Infinity;
		private float InteractGraceTime = 0.1f;
		private Vector3 interactionPosition = Vector3.zero;

		// Handle escape to menu
		private bool _escapeRequested = false;
		private bool _escapeConsumed = false;
		private bool _escapedThisFrame = false;
		private float _timeSinceEscapeRequested = Mathf.Infinity;
		private float EscapeGraceTime = 0.1f;

		// Handle firing
		private bool _fireRequested = false;
		private bool _fireConsumed = false;
		private bool _firedThisFrame = false;
		private float _timeSinceFireRequested = Mathf.Infinity;
		private float FireGraceTime = 0.1f;
		private Vector3 firePosition = Vector3.zero;

		// Handle crouching
		private bool _shouldBeCrouching = false;
		private bool _isCrouching = false;

		// Used to play extra idle animations after a few seconds
		private float idleTime = 0.0f;
		private float boredTime = 5.0f;

		// Determines whether the dying player falls fowards or backwards
		private bool HitFront = true;

		private AudioSource audioSource;

		private void OnEnable()
		{
			LevelManager.OnKinCharacterDeath += OnKinCharacterDeath;
			CheatManager.OnWarpPlayer += OnWarpPlayer;
			BlockManager.OnBlockBump += OnBlockBump;
		}

		private void OnDisable()
		{
			LevelManager.OnKinCharacterDeath -= OnKinCharacterDeath;
			CheatManager.OnWarpPlayer -= OnWarpPlayer;
			BlockManager.OnBlockBump -= OnBlockBump;
		}

		private void Start()
		{
			// Handle initial state
			TransitionToState(CharacterState.Default);

			// Assign the characterController to the motor
			Motor.CharacterController = this;

			audioSource = GetComponent<AudioSource>();
		}

		/// <summary>
		/// Handles movement state transitions and enter/exit callbacks
		/// </summary>
		public void TransitionToState(CharacterState newState)
		{
			CharacterState tmpInitialState = CurrentCharacterState;
			OnStateExit(tmpInitialState, newState);
			CurrentCharacterState = newState;
			OnStateEnter(newState, tmpInitialState);
		}

		/// <summary>
		/// Event when entering a state
		/// </summary>
		public void OnStateEnter(CharacterState state, CharacterState fromState)
		{
			// TODO: move animations to AfterUpdate method
			switch (state)
			{
				case CharacterState.Default:
					{
						OnChangeUI?.Invoke(1);
						break;
					}
				case CharacterState.Dying:
					{
						_moveInputVector = Vector3.zero;
						CharacterAnimator.SetFloat("Forward", 0.0f);
						if (HitFront)
							CharacterAnimator.SetTrigger("DieBack");
						else
							CharacterAnimator.SetTrigger("DieForward");
						break;
					}
				case CharacterState.Revive:
					{
						_moveInputVector = Vector3.zero;
						CharacterAnimator.SetTrigger("Revive");
						break;
					}
				case CharacterState.Pause:
					{
						// Stop the player
						_moveInputVector = Vector3.zero;
						CharacterAnimator.SetFloat("Forward", 0.0f);
						CharacterAnimator.SetBool("Crouch", false);

						break;
					}
			}
		}

		/// <summary>
		/// Event when exiting a state
		/// </summary>
		public void OnStateExit(CharacterState state, CharacterState toState)
		{
			switch (state)
			{
				case CharacterState.Default:
					{
						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						CharacterAnimator.SetBool("Revive", false);
						break;
					}
				case CharacterState.Pause:
					{
						// Hide UI
						OnChangeUI?.Invoke(-1);
						break;
					}
			}
		}

		/// <summary>
		/// This is called every frame in order to tell the character what its inputs are
		/// </summary>
		public void SetInputs(ref PlayerCharacterInputs inputs)
		{
			// Clamp input
			var moveInputVector = Vector3.ClampMagnitude(new Vector3(0f, 0f, inputs.MoveAxisForward), 1f);

			// Calculate camera direction and rotation on the character plane
			var cameraPlanarDirection = Vector3.ProjectOnPlane(Quaternion.identity * Vector3.forward, Motor.CharacterUp).normalized;
			if (Math.Abs(cameraPlanarDirection.sqrMagnitude) < 0.001)
			{
				cameraPlanarDirection = Vector3.ProjectOnPlane(Quaternion.identity * Vector3.up, Motor.CharacterUp).normalized;
			}
			var cameraPlanarRotation = Quaternion.LookRotation(cameraPlanarDirection, Motor.CharacterUp);

			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						// Move and look inputs
						_moveInputVector = cameraPlanarRotation * moveInputVector;

						// Jumping input
						if (inputs.JumpDown)
						{
							_timeSinceJumpRequested = 0f;
							_jumpRequested = true;
						}

						if (inputs.InteractDown)
						{
							_timeSinceInteractRequested = 0f;
							_interactRequested = true;
						}

						if (inputs.FireDown)
						{
							_timeSinceFireRequested = 0f;
							_fireRequested = true;
						}
						if (inputs.EscapeDown)
						{
							_timeSinceEscapeRequested = 0f;
							_escapeRequested = true;
						}

						// Crouching input
						if (inputs.CrouchDown)
						{
							_shouldBeCrouching = true;

							if (!_isCrouching)
							{
								_isCrouching = true;
								Motor.SetCapsuleDimensions(0.25f, 0.85f, 0.425f);
							}
						}
						else if (inputs.CrouchUp)
						{
							_shouldBeCrouching = false;
						}

						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is called before the character begins its movement update
		/// </summary>
		public void BeforeCharacterUpdate(float deltaTime)
		{
		}

		/// <summary>
		/// Called by KinematicCharacterMotor during its update cycle, set player rotation
		/// </summary>
		public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
		{
			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						// If player is moving, face in that direction
						if (!_moveInputVector.Equals(Vector3.zero))
							currentRotation = Quaternion.LookRotation(_moveInputVector, Motor.CharacterUp);
						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		/// <summary>
		/// Called by KinematicCharacterMotor during its update cycle, set player velocity
		/// </summary>
		public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
		{
			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						// Ground movement
						if (Motor.GroundingStatus.IsStableOnGround)
						{
							var currentVelocityMagnitude = currentVelocity.magnitude;

							var effectiveGroundNormal = Motor.GroundingStatus.GroundNormal;
							if (currentVelocityMagnitude > 0f && Motor.GroundingStatus.SnappingPrevented)
							{
								// Take the normal from where we're coming from
								var groundPointToCharacter = Motor.TransientPosition - Motor.GroundingStatus.GroundPoint;
								if (Vector3.Dot(currentVelocity, groundPointToCharacter) >= 0f)
								{
									effectiveGroundNormal = Motor.GroundingStatus.OuterGroundNormal;
								}
								else
								{
									effectiveGroundNormal = Motor.GroundingStatus.InnerGroundNormal;
								}
							}

							// Reorient velocity on slope
							currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, effectiveGroundNormal) * currentVelocityMagnitude;

							// Calculate target velocity
							var inputRight = Vector3.Cross(_moveInputVector, Motor.CharacterUp);
							var reorientedInput = Vector3.Cross(effectiveGroundNormal, inputRight).normalized * _moveInputVector.magnitude;
							var targetMovementVelocity = reorientedInput * MaxStableMoveSpeed;
							if (_isCrouching)
								targetMovementVelocity = reorientedInput * MaxCrouchMoveSpeed;

							// Smooth movement Velocity
							currentVelocity = Vector3.Lerp(currentVelocity, targetMovementVelocity, 1f - Mathf.Exp(-StableMovementSharpness * deltaTime));
						}
						// Air movement
						else
						{
							// Add move input
							if (_moveInputVector.sqrMagnitude > 0f)
							{
								var addedVelocity = _moveInputVector * (AirAccelerationSpeed * deltaTime);

								var currentVelocityOnInputsPlane = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);

								// Limit air velocity from inputs
								if (currentVelocityOnInputsPlane.magnitude < MaxAirMoveSpeed)
								{
									// clamp addedVel to make total vel not exceed max vel on inputs plane
									var newTotal = Vector3.ClampMagnitude(currentVelocityOnInputsPlane + addedVelocity, MaxAirMoveSpeed);
									addedVelocity = newTotal - currentVelocityOnInputsPlane;
								}
								else
								{
									// Make sure added vel doesn't go in the direction of the already-exceeding velocity
									if (Vector3.Dot(currentVelocityOnInputsPlane, addedVelocity) > 0f)
									{
										addedVelocity = Vector3.ProjectOnPlane(addedVelocity, currentVelocityOnInputsPlane.normalized);
									}
								}

								// Prevent air-climbing sloped walls
								if (Motor.GroundingStatus.FoundAnyGround)
								{
									if (Vector3.Dot(currentVelocity + addedVelocity, addedVelocity) > 0f)
									{
										var perpendicularObstructionNormal = Vector3.Cross(Vector3.Cross(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal), Motor.CharacterUp).normalized;
										addedVelocity = Vector3.ProjectOnPlane(addedVelocity, perpendicularObstructionNormal);
									}
								}

								// Apply added velocity
								currentVelocity += addedVelocity;
							}

							// Gravity
							currentVelocity += Gravity * deltaTime;

							// Drag
							currentVelocity *= (1f / (1f + (Drag * deltaTime)));

							// When interacting, player 'floats' slightly
							if (_interactedThisFrame)
							{
								// TODO: Slightly bump character up each interaction
								//var jumpDirection = Motor.CharacterUp;
								//currentVelocity  += (jumpDirection * 0.74f) - Vector3.Project(currentVelocity, Motor.CharacterUp);
							}
						}

						// Handle jumping
						_jumpedThisFrame = false;
						_timeSinceJumpRequested += deltaTime;
						if (_jumpRequested)
						{
							// See if we actually are allowed to jump
							if (!_jumpConsumed && ((Motor.GroundingStatus.IsStableOnGround) || _timeSinceLastAbleToJump <= JumpPostGroundingGraceTime))
							{
								// Calculate jump direction before un-grounding
								var jumpDirection = Motor.CharacterUp;
								if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
								{
									jumpDirection = Motor.GroundingStatus.GroundNormal;
								}

								// Makes the character skip ground probing/snapping on its next update. 
								// If this line weren't here, the character would remain snapped to the ground when trying to jump. Try commenting this line out and see.
								Motor.ForceUnground();

								// Add to the return velocity and reset jump state
								currentVelocity += (jumpDirection * JumpUpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
								currentVelocity += (_moveInputVector * JumpScalableForwardSpeed);
								_jumpRequested = false;
								_jumpConsumed = true;
								_jumpedThisFrame = true;
							}
						}

						// Take into account additive velocity
						if (_internalVelocityAdd.sqrMagnitude > 0f)
						{
							currentVelocity += _internalVelocityAdd;
							_internalVelocityAdd = Vector3.zero;
						}

						// Clamp velocity to Y/Z axes
						currentVelocity.x = 0.0f;

						break;
					}
				case CharacterState.Dying:
					{
						currentVelocity = Vector3.zero;
						_jumpRequested = false;
						_jumpConsumed = true;
						_jumpedThisFrame = true;
						break;
					}
				case CharacterState.Revive:
					{
						currentVelocity = Vector3.zero;
						_jumpRequested = false;
						_jumpConsumed = true;
						_jumpedThisFrame = true;
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		/// <summary>
		/// (Called by KinematicCharacterMotor during its update cycle)
		/// This is called after the character has finished its movement update
		/// </summary>
		public void AfterCharacterUpdate(float deltaTime)
		{
			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						CharacterAnimator.SetFloat("Forward", System.Math.Abs(_moveInputVector.z));
						CharacterAnimator.SetBool("OnGround", Motor.GroundingStatus.IsStableOnGround);
						CharacterAnimator.SetBool("Crouch", _isCrouching); // Not a trigger, must be held down
						if (_jumpedThisFrame)
							CharacterAnimator.SetTrigger("Jump");
						if (_interactRequested)
							CharacterAnimator.SetTrigger("Interact");
						if (_fireRequested)
							CharacterAnimator.SetTrigger("Fire");

						// If character is bored, play extra idle animations
						if (Math.Abs(_moveInputVector.z) < 0.1)
							idleTime += deltaTime;
						else
							idleTime = 0.0f;

						if (idleTime > boredTime)
                        {
							idleTime = 0.0f;
							if (UnityEngine.Random.Range(0.0f, 1.0f) < 0.5)
								CharacterAnimator.SetTrigger("Bored2");
							else
								CharacterAnimator.SetTrigger("Bored3");
						}

						// Handle jump-related values
						{
							// Handle jumping pre-ground grace period
							if (_jumpRequested && _timeSinceJumpRequested > JumpPreGroundingGraceTime)
							{
								_jumpRequested = false;
							}

							if (Motor.GroundingStatus.IsStableOnGround)
							{
								// If we're on a ground surface, reset jumping values
								if (!_jumpedThisFrame)
								{
									_jumpConsumed = false;

								}
								_timeSinceLastAbleToJump = 0f;

							}
							else
							{
								// Keep track of time since we were last able to jump (for grace period)
								_timeSinceLastAbleToJump += deltaTime;
							}
						}

						// Handle interact-related requests
						_interactedThisFrame = false;
						_timeSinceInteractRequested += deltaTime;
						if (_interactRequested)
						{
							if (!_interactConsumed || _timeSinceInteractRequested <= InteractGraceTime)
							{
								_interactRequested = false;
								_interactConsumed = true;
								_interactedThisFrame = true;
							}
						}

						// Handle fire-related requests
						_firedThisFrame = false;
						_timeSinceFireRequested += deltaTime;
						if (_fireRequested)
						{
							if (!_fireConsumed || _timeSinceFireRequested <= FireGraceTime)
							{
								_fireRequested = false;
								_fireConsumed = true;
								_firedThisFrame = true;
							}
						}

						// Handle un-crouching
						if (_isCrouching && !_shouldBeCrouching)
						{
							// Do an overlap test with the character's standing height to see if there are any obstructions
							// TODO: Load and store these from player capsule
							Motor.SetCapsuleDimensions(0.25f, 1.7f, 0.85f);
							if (Motor.CharacterOverlap(
								Motor.TransientPosition,
								Motor.TransientRotation,
								_probedColliders,
								Motor.CollidableLayers,
								QueryTriggerInteraction.Ignore) > 0)
							{
								// If obstructions, just stick to crouching dimensions
								Motor.SetCapsuleDimensions(0.25f, 0.85f, 0.425f);
							}
							else
							{
								// If no obstructions, un-crouch
								_isCrouching = false;
							}
						}

						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		public void PostGroundingUpdate(float deltaTime)
		{
			// Handle landing and leaving ground
			if (Motor.GroundingStatus.IsStableOnGround && !Motor.LastGroundingStatus.IsStableOnGround)
			{
				//OnLanded();
			}
			else if (!Motor.GroundingStatus.IsStableOnGround && Motor.LastGroundingStatus.IsStableOnGround)
			{
				//OnLeaveStableGround();
			}
		}

		public bool IsColliderValidForCollisions(Collider coll)
		{
			return true;
		}

		public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
		{
		}

		public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
		{
			Rigidbody r = hitCollider.attachedRigidbody;
			if (r)
			{
				Vector3 relativeVel = Vector3.Project(r.velocity, hitNormal) - Vector3.Project(Motor.Velocity, hitNormal);
			}
		}

		public void AddVelocity(Vector3 velocity)
		{
			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						_internalVelocityAdd += velocity;
						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport)
		{
		}

		public void OnDiscreteCollisionDetected(Collider hitCollider)
		{
			if (hitCollider.CompareTag("Enemy"))
			{
				if (CurrentCharacterState != CharacterState.Dying && CurrentCharacterState != CharacterState.Revive)
				{
					Debug.Log("Player killed by " + hitCollider.name);
					// TODO: Get hit direction
					TransitionToState(CharacterState.Dying);
				}
			}
		}

		// Must be public - called from Animation Events
		public void PlayMoveSound()
		{
			// TODO: Change sound based on surface type?
			if (audioSource && moveSound) audioSource.PlayOneShot(moveSound);
		}

		// Capture position where interaction occurred early
		public void SetFirePosition()
		{
			// Calculate block hit by wand
			firePosition = Vector3.zero;

			// Get facing direction and slightly in front of player
			firePosition = new Vector3(0.0f, transform.position.y + 0.5f, transform.position.z + 0.66f * transform.forward.z);
		}


		// Capture position where interaction occurred early
		public void SetInteractionPosition()
		{
			// Calculate block hit by wand
			interactionPosition = Vector3.zero;

			// Get facing direction and move block to center of next spot
			if (transform.forward.z > 0f)
			{
				var newZPos = Mathf.CeilToInt(transform.position.z + 1);
				if (newZPos % 2 != 0)
				{
					newZPos += 1;
				}
				interactionPosition = new Vector3(interactionPosition.x, interactionPosition.y, newZPos);
			}
			else
			{
				var newZPos = (int)Mathf.FloorToInt(transform.position.z - 1);
				if (newZPos % 2 != 0)
				{
					newZPos -= 1;
				}
				interactionPosition = new Vector3(interactionPosition.x, interactionPosition.y, newZPos);
			}

			// Calculate y position
			if (_isCrouching)
			{
				var newYPos = Mathf.CeilToInt(transform.position.y - 1);
				if (newYPos % 2 == 0)
				{
					newYPos -= 1;
				}
				interactionPosition = new Vector3(interactionPosition.x, newYPos, interactionPosition.z);
			}
			else
			{
				var newYPos = Mathf.CeilToInt(transform.position.y + 1);
				if (newYPos % 2 == 0)
				{
					newYPos -= 1;
				}
				interactionPosition = new Vector3(interactionPosition.x, newYPos, interactionPosition.z);
			}
		}

		// Must be public - called from Animation Events
		public void NormalInteraction()
		{
			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						// Handle interacting
						if (_interactRequested && _timeSinceInteractRequested > InteractGraceTime)
						{
							_interactRequested = false;
						}

						// Perform interacting
						// Code removed

						// Interact done, reset interact values
						if (!_interactedThisFrame)
						{
							_interactConsumed = false;
						}
						_timeSinceInteractRequested = 0f;
						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		// Must be public - called from Animation Events
		public void NormalFire()
		{
			// TODO: does player have fireballs collected?
			// TODO: is another fireball still active?
			switch (CurrentCharacterState)
			{
				case CharacterState.Default:
					{
						// Handle firing
						if (_fireRequested && _timeSinceFireRequested > FireGraceTime)
						{
							_fireRequested = false;
						}

						// Spawn fireball from pool
						// Code removed


						// Firing done, reset firing values
						if (!_firedThisFrame)
						{
							_fireConsumed = false;
						}
						_timeSinceFireRequested = 0f;
						break;
					}
				case CharacterState.Dying:
					{
						break;
					}
				case CharacterState.Revive:
					{
						break;
					}
				case CharacterState.Pause:
					{
						break;
					}
			}
		}

		// Must be public - called from Animation Events
		public void CrouchInteraction()
		{
			NormalInteraction();
		}

		// Must be public - called from Animation Events
		public void CrouchFire()
		{
			NormalFire();
		}
		public void DeathStart()
		{
			// TODO: Play death sound/effect(s)
		}

		public void DeathComplete()
		{
			// Get last checkpoint location
			OnWarpPlayer(GameObject.FindGameObjectWithTag("CheckPoint"));
			TransitionToState(CharacterState.Revive);
		}

		public void OnWarpPlayer(GameObject warpPoint)
		{
			Motor.SetPositionAndRotation(warpPoint.transform.position, warpPoint.transform.rotation);
		}

		public void ReviveComplete()
		{
			TransitionToState(CharacterState.Default);
		}

		public void OnLeaveStableGround()
		{
		}
		public void OnLanded()
		{
			if (audioSource && landSound) audioSource.PlayOneShot(landSound);
		}

		public void OnInteracted()
		{
			if (audioSource && interactSound) audioSource.PlayOneShot(interactSound);
		}

		public void OnFired()
		{
			if (audioSource && fireSound) audioSource.PlayOneShot(fireSound);
		}

		public void OnKinCharacterDeath(RaycastHit hitInfo)
		{
			// Already dying
			if (CurrentCharacterState == CharacterState.Dying) return;

			if (Math.Abs(hitInfo.point.z - transform.position.z) > 0)
			{
				Debug.Log("HitFront: " + hitInfo.point + " " + transform.position);
				HitFront = true;
			}
			else
			{
				Debug.Log("HitBack: " + hitInfo.point + " " + transform.position);
				HitFront = false;
			}

			// Decrease life count			
			OnUpdateHUDLives?.Invoke(-1);

			// Reset the level
			OnResetLevel?.Invoke();

			TransitionToState(CharacterState.Dying);
		}

		public void OnBlockBump(bool IsCracked)
		{
			if (audioSource && bumpSound && !IsCracked) audioSource.PlayOneShot(bumpSound);
			if (audioSource && breakSound && IsCracked) audioSource.PlayOneShot(breakSound);
		}

		// Must be public - called from Animation Events
		public void EscapeSequence()
		{
			// Escape done, reset escape values
			if (!_escapedThisFrame)
			{
				_escapeConsumed = false;
			}
			_timeSinceEscapeRequested = 0f;

			TransitionToState(CharacterState.Pause);
		}
	}
}
