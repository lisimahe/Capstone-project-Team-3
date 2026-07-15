using UnityEngine;

public class FollowScript : MonoBehaviour
{
    public float Speed;

    public Transform Target;

    void Update()
    {
        // Ignore y position of target and allow natural fall of gravity
        Vector3 TargetPosition = new Vector3(Target.position.x, transform.position.y, Target.position.z);

        // Calculate distance to move
        float distance = Speed * Time.deltaTime; 

        transform.position = Vector3.MoveTowards(transform.position, TargetPosition, distance);

        RotateTowards();
    }


    private void RotateTowards()
    {
        var lookPos = Target.position - transform.position;
        lookPos.y = 0;
        var targetRotation = Quaternion.LookRotation(lookPos);

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Speed * 4f * Time.deltaTime);
    }



}
