using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reflaection
{
	public class KinPlayer : MonoBehaviour
	{
		//private Transform CameraFollowPoint;
		private KinPlayerCamera StaticCamera;
		private KinPlayerController Character;
		private PlayerCharacterInputs characterInputs;
		
		private void Start()
		{
			Cursor.lockState = CursorLockMode.Confined;
			Cursor.visible = false;
			Character = GameObject.FindWithTag("Player").GetComponent<KinPlayerController>();
			
			characterInputs = new PlayerCharacterInputs
			{
				MoveAxisForward = 0.0f,
				JumpDown = false,
				CrouchDown = false,
				CrouchUp = false,
				InteractDown = false,
				EscapeDown = false,
				FireDown = false,
			};
		}

		public void OnMovement(InputAction.CallbackContext value)
		{
			Vector2 inputMovement = value.ReadValue<Vector2>();
			characterInputs.MoveAxisForward = inputMovement.x;
			Character.SetInputs(ref characterInputs);
		}

		public void OnCrouch(InputAction.CallbackContext value)
		{
			characterInputs.CrouchDown = value.started;
			characterInputs.CrouchUp = value.canceled;
			Character.SetInputs(ref characterInputs);
		}

		public void OnAttack(InputAction.CallbackContext value)
		{
			characterInputs.FireDown = value.started;
			Character.SetInputs(ref characterInputs);
		}

		public void OnJump(InputAction.CallbackContext value)
		{
			characterInputs.JumpDown = value.started;
			Character.SetInputs(ref characterInputs);
		}

		public void OnInteract(InputAction.CallbackContext value)
		{
			characterInputs.InteractDown = value.started;
			Character.SetInputs(ref characterInputs);
		}

		public void OnTogglePause(InputAction.CallbackContext value)
		{
			characterInputs.EscapeDown = value.started;
			Character.SetInputs(ref characterInputs);
		}

		public void OnControlsChanged()
		{
			Debug.Log("OnControlsChanged");
		}

		public void OnDeviceLost()
		{
			Debug.Log("OnDeviceLost");
		}

		public void OnDeviceRegained()
		{
			Debug.Log("OnDeviceRegained");
			StartCoroutine(WaitForDeviceToBeRegained());
		}

		IEnumerator WaitForDeviceToBeRegained()
		{
			yield return new WaitForSeconds(0.1f);
		}
	}
}
