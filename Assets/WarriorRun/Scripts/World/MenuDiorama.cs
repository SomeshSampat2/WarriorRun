using UnityEngine;

namespace WarriorRun.World
{
    /// <summary>
    /// Menu-scene backdrop: a slow drift down the track plus an idle runner.
    /// The camera pans gently while the character does its idle cycle.
    /// </summary>
    public class MenuDiorama : MonoBehaviour
    {
        [SerializeField] float swayAmount = 1.1f;
        [SerializeField] float swaySpeed = 0.22f;
        [SerializeField] float bobAmount = 0.15f;
        [SerializeField] float bobSpeed = 0.4f;

        Vector3 startPos;
        Quaternion startRot;

        void Start()
        {
            startPos = transform.position;
            startRot = transform.rotation;
        }

        void Update()
        {
            float x = Mathf.Sin(Time.time * swaySpeed) * swayAmount;
            float y = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            transform.position = startPos + new Vector3(x, y, 0f);
            // gentle yaw so the view pans across the runner and track
            transform.rotation = startRot * Quaternion.Euler(0f, Mathf.Sin(Time.time * swaySpeed) * 6f, 0f);
        }
    }
}
