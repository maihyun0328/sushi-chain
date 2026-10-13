using UnityEngine;
using UnityEngine.Rendering;

public class VFXManager : MonoBehaviour
{
    public static VFXManager i;
    [SerializeField] GameObject LightVFX;
    [SerializeField] GameObject PurpleVFX;
    [SerializeField] GameObject LightPool;
    [SerializeField] GameObject PurplePool;
    Vector3 L1VFX = new Vector3(-1.33f, 3.12f, 0);
    Vector3 L2VFX = new Vector3(-1.33f, 1.1f, 0);
    Vector3 R1VFX = new Vector3(1.33f, 3.12f, 0);
    Vector3 R2VFX = new Vector3(1.33f, 1.1f, 0);
    void Awake()
    {
        i = this;
        CreateSushiVFX(10);
    }
    void Start()
    {
        
    }

    void Update()
    {
        
    }
    void CreateSushiVFX(int count = 1)
    {
        for(int i = 0; i < count; i++)
        {
            Instantiate(LightVFX, LightPool.transform).SetActive(false);
            Instantiate(PurpleVFX, PurplePool.transform).SetActive(false);
        }
    }
    public void SushiVFX(SUSHI_TYPE sushi)
    {
        if(LightPool.transform.childCount == 0 || PurplePool.transform.childCount == 0) CreateSushiVFX();
        GameObject Lightobj = LightPool.transform.GetChild(0).gameObject;
        GameObject Purpleobj = PurplePool.transform.GetChild(0).gameObject;
        Lightobj.transform.SetParent(null);
        Purpleobj.transform.SetParent(null);
        Lightobj.SetActive(true);
        Purpleobj.SetActive(true);
        switch(sushi)
        {
            case SUSHI_TYPE.L1:
                Lightobj.transform.position = L1VFX;
                Purpleobj.transform.position = L1VFX;
                break;
            case SUSHI_TYPE.L2:
                Lightobj.transform.position = L2VFX;
                Purpleobj.transform.position = L2VFX;
                break;
            case SUSHI_TYPE.R1:
                Lightobj.transform.position = R1VFX;
                Purpleobj.transform.position = R1VFX;
                break;
            case SUSHI_TYPE.R2:
                Lightobj.transform.position = R2VFX;
                Purpleobj.transform.position = R2VFX;
                break;
        }
    }
    public void ReturnVFX(GameObject obj, VFX_TYPE vfx)
    {
        switch(vfx)
        {
            case VFX_TYPE.LIGHT:
                obj.transform.SetParent(LightPool.transform);
                break;
            case VFX_TYPE.PURPLE:
                obj.transform.SetParent(PurplePool.transform);
                break;
        }
        obj.SetActive(false);
    }
}
