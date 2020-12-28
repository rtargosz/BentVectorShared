using System.Collections.Generic;
using UnityEngine;

namespace Reflaection
{
	public class KinPlayerCamera : MonoBehaviour
	{
		public Vector3 _followOffset = new Vector3(6.0f, 0.75f, 0f);
		public Vector3 _followRotation = new Vector3(20.0f, -90.0f, 0.0f);
		public GameObject FollowObject;

		public void Update()
		{
			// Calculate the precise follow position (no smoothing)
			transform.position = Vector3.Lerp(transform.position, FollowObject.transform.position, 1f) + _followOffset;
			transform.rotation = Quaternion.Euler(_followRotation);
		}
	}
}
