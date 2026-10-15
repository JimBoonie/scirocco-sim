using UnityEngine;
public class FollowCam : MonoBehaviour
{ public Transform target; [HideInInspector] public float lookYaw, lookPitch;   // look axes (deg) from InputRouter
  // @description - Unity callback: subscribes to the car teleport event so the camera can snap after a reset.
  // @return {Void} - nothing
  void OnEnable() { CarController.Teleported += OnTeleport; }
  // @description - Unity callback: unsubscribes from the car teleport event.
  // @return {Void} - nothing
  void OnDisable() { CarController.Teleported -= OnTeleport; }
  // @description - Snaps the camera behind the car if the car that teleported is the followed one.
  // @param {CarController} car - the car that was reset or teleported
  // @return {Void} - nothing
  void OnTeleport(CarController car) { if (target && car && target == car.transform) Snap(); }
  // @description - Jumps straight behind the car (6 m back, 2.2 m up) and clears the look offsets, instead of lerping from the old position after a respawn.
  // @return {Void} - nothing
  public void Snap() { lookYaw = lookPitch = 0f; if (!target) return; transform.position = target.position - target.forward * 6f + Vector3.up * 2.2f; transform.LookAt(target.position + Vector3.up * 0.6f); }
  // @description - Chase camera: target position is 6 m behind and 2.2 m above the car, rotated by the look yaw/pitch, approached with an exponential lerp (rate 5 per second) while always looking at a point 0.6 m above the car.
  // @return {Void} - nothing
  void LateUpdate() { if (!target) return;
  Vector3 directionBehindCar = Quaternion.AngleAxis(lookYaw, Vector3.up) * Quaternion.AngleAxis(-lookPitch, target.right) * (-target.forward);
  Vector3 desiredPosition = target.position + directionBehindCar * 6f + Vector3.up * 2.2f;
  transform.position = Vector3.Lerp(transform.position, desiredPosition, 5f * Time.deltaTime); transform.LookAt(target.position + Vector3.up * 0.6f); } }
