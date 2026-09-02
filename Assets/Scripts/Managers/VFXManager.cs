using UnityEngine;
using UnityEngine.Rendering;

public class VFXManager : MonoBehaviour
{
    public static VFXManager i;
    [SerializeField] GameObject LightVFX;
    [SerializeField] GameObject PurpleVFX;
    [SerializeField] GameObject LightPool;
    [SerializeField] GameObject PurplePool;
    Vector3 LeftVFX = new Vector3(-1.33f, 1.1f, 0);
    Vector3 RightVFX = new Vector3(1.33f, 1.1f, 0);
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
    public void SushiVFX(NOTE_DIRECT dir)
    {
        if(LightPool.transform.childCount == 0 || PurplePool.transform.childCount == 0) CreateSushiVFX();
        GameObject Lightobj = LightPool.transform.GetChild(0).gameObject;
        GameObject Purpleobj = PurplePool.transform.GetChild(0).gameObject;
        Lightobj.transform.SetParent(null);
        Purpleobj.transform.SetParent(null);
        Lightobj.SetActive(true);
        Purpleobj.SetActive(true);
        switch(dir)
        {
            case NOTE_DIRECT.LEFT:
                Lightobj.transform.position = LeftVFX;
                Purpleobj.transform.position = LeftVFX;
                break;
            case NOTE_DIRECT.RIGHT:
                Lightobj.transform.position = RightVFX;
                Purpleobj.transform.position = RightVFX;
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
