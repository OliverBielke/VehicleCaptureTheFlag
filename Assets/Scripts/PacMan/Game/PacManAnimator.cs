using UnityEngine;
using Random = UnityEngine.Random;

namespace PacMan.Game
{
    public class PacManAnimator : MonoBehaviour
    {
        public Transform upperLip;
        public Transform lowerLip;
        public float stepSpeed = 0.1f;
        private float t;
        private bool increasing;

        void Start()
        {
            t = Random.Range(0, 1);
            increasing = Random.Range(0, 1) > 0.5;
        }


        void FixedUpdate()
        {
            t += stepSpeed * (increasing ? 1f : -1f);

            upperLip.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, 90+Mathf.Lerp(182.461f, 145, t)));
            lowerLip.transform.localRotation = Quaternion.Euler(new Vector3(0, 0, 90+Mathf.Lerp(0, 35.589f, t)));

            if (t < 0 || t > 1.0f)
            {
                increasing = !increasing;
            }
        }
    }
}