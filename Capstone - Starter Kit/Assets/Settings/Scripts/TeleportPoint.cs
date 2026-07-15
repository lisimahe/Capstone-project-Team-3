using UnityEngine;
using System.Collections;


public class TeleportPoint : MonoBehaviour {

    //private float dimmingSpeed = 16;
    //private float fullIntensity = 4;
    //private float lowIntensity = 0.5f;
    //private float lastLookAtTime = 0;

    private float defaultHeight = 0.25f;

    public Transform teleportTransform;

    [SerializeField] private GameObject rainParticle;

    bool isGlowing = false;

    
    public Vector3 GetTeleportPosition()
    {
        return teleportTransform.position;
    }
   

    /*
	// Update is called once per frame
	void Update ()
    {
        if (currentPoint == this)
        {

        }

        float intensity = Mathf.SmoothStep(fullIntensity, lowIntensity, (Time.time - lastLookAtTime) * dimmingSpeed);
        GetComponent<MeshRenderer>().material.SetFloat("_Intensity", intensity);
	}
    */

    public void OnLookAt(bool isValid)
    {

            if (isGlowing == false)
            {
                isGlowing = true;
                rainParticle.SetActive(true);
                //teleportTransform.DOScaleY(1f, 0.5f);
            }

       
    }

    public void Reset()
    {

        if (isGlowing)
        {
            isGlowing = false;
            rainParticle.SetActive(false);
            //teleportTransform.DOScaleY(defaultHeight, 0.5f);
        }
    }
     
}
