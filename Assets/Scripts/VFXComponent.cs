using UnityEngine;

public class VFXComponent : MonoBehaviour
{
    [SerializeField] VFX_TYPE type;

    void OnParticleSystemStopped()
    {
        VFXManager.i.ReturnVFX(gameObject, type);
    }
}
