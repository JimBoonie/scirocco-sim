using UnityEngine;
public class FollowCam : MonoBehaviour
{ public Transform target; [HideInInspector] public float lookYaw, lookPitch;   // look axes (deg) from InputRouter
  // @description - Handles the enable.
  // @return {Void} - nothing
  void OnEnable() { CarController.Teleported += OnTeleport; } void OnDisable() { CarController.Teleported -= OnTeleport; }
  // @description - Handles the teleport.
  // @param {CarController} car - car
  // @return {Void} - nothing
  void OnTeleport(CarController car) { if (target && car && target == car.transform) Snap(); }
  // @description - jump straight behind the car (no lerp from the old position: after a respawn the camera was swinging in from up to ~1 km away)
  // @return {Void} - nothing
  public void Snap() { lookYaw = lookPitch = 0f; if (!target) return; transform.position = target.position - target.forward * 6f + Vector3.up * 2.2f; transform.LookAt(target.position + Vector3.up * 0.6f); }
  // @description - Late update.
  // @return {Void} - nothing
  void LateUpdate() { if (!target) return;
  Vector3 back = Quaternion.AngleAxis(lookYaw, Vector3.up) * Quaternion.AngleAxis(-lookPitch, target.right) * (-target.forward);
  Vector3 want = target.position + back * 6f + Vector3.up * 2.2f;
  transform.position = Vector3.Lerp(transform.position, want, 5f * Time.deltaTime); transform.LookAt(target.position + Vector3.up * 0.6f); } }
