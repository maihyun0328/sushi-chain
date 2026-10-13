using UnityEngine;

public class SushiPoolComponent : MonoBehaviour
{
    [SerializeField] GameObject Active_L1_Short;
    [SerializeField] GameObject Active_L2_Short;
    [SerializeField] GameObject Active_R1_Short;
    [SerializeField] GameObject Active_R2_Short;

    [SerializeField] GameObject Active_L1_Long;
    [SerializeField] GameObject Active_L2_Long;
    [SerializeField] GameObject Active_R1_Long;
    [SerializeField] GameObject Active_R2_Long;

    [SerializeField] GameObject L1_Short_Prefab;
    [SerializeField] GameObject L2_Short_Prefab;
    [SerializeField] GameObject R1_Short_Prefab;
    [SerializeField] GameObject R2_Short_Prefab;

    [SerializeField] GameObject L1_Long_Prefab;
    [SerializeField] GameObject L2_Long_Prefab;
    [SerializeField] GameObject R1_Long_Prefab;
    [SerializeField] GameObject R2_Long_Prefab;

    [SerializeField] GameObject Inactive_L1_Short;
    [SerializeField] GameObject Inactive_L2_Short;
    [SerializeField] GameObject Inactive_R1_Short;
    [SerializeField] GameObject Inactive_R2_Short;

    [SerializeField] GameObject Inactive_L1_Long;
    [SerializeField] GameObject Inactive_L2_Long;
    [SerializeField] GameObject Inactive_R1_Long;
    [SerializeField] GameObject Inactive_R2_Long;
    Vector3 L1Vector = new Vector3(-11, 3.87f, -0.82f);
    Vector3 L2Vector = new Vector3(-11, 1.03f, -0.82f);
    Vector3 R1Vector = new Vector3(11, 3.87f, 0.338f);
    Vector3 R2Vector = new Vector3(11, 1.03f, 0.338f);
    Quaternion LeftEuler = Quaternion.Euler(0, 270, 0);
    Quaternion RightEuler = Quaternion.Euler(0, 90, 0);
    
    public static SushiPoolComponent i;
    void Awake()
    {
        i = this;
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SushiCreate(SUSHI_TYPE.L1, NOTE_TYPE.SHORT, 15);
        SushiCreate(SUSHI_TYPE.L2, NOTE_TYPE.SHORT, 15);
        SushiCreate(SUSHI_TYPE.R1, NOTE_TYPE.SHORT, 15);
        SushiCreate(SUSHI_TYPE.R2, NOTE_TYPE.SHORT, 15);
        SushiCreate(SUSHI_TYPE.L1, NOTE_TYPE.LONG, 15);
        SushiCreate(SUSHI_TYPE.L2, NOTE_TYPE.LONG, 15);
        SushiCreate(SUSHI_TYPE.R1, NOTE_TYPE.LONG, 15);
        SushiCreate(SUSHI_TYPE.R2, NOTE_TYPE.LONG, 15);
    }
    public void SushiCreate(SUSHI_TYPE sushi, NOTE_TYPE note, int val = 1)
    {
        for(int i = 0; i < val; i++)
        {
            switch(sushi)
            {
                case SUSHI_TYPE.L1:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(L1_Short_Prefab, L1Vector, LeftEuler, Inactive_L1_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(L1_Long_Prefab, L1Vector, LeftEuler, Inactive_L1_Long.transform).SetActive(false);
                            break;
                    }
                    break;
                case SUSHI_TYPE.L2:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(L2_Short_Prefab, L2Vector, LeftEuler, Inactive_L2_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(L2_Long_Prefab, L2Vector, LeftEuler, Inactive_L2_Long.transform).SetActive(false);
                            break;
                    }
                    break;
                case SUSHI_TYPE.R1:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(R1_Short_Prefab, R1Vector, RightEuler, Inactive_R1_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(R1_Long_Prefab, R1Vector, RightEuler, Inactive_R1_Long.transform).SetActive(false);
                            break;
                    }
                    break;
                case SUSHI_TYPE.R2:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(R2_Short_Prefab, R2Vector, RightEuler, Inactive_R2_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(R2_Long_Prefab, R2Vector, RightEuler, Inactive_R2_Long.transform).SetActive(false);
                            break;
                    }
                    break;
            }
        }
    }
    public void UseSushi(SUSHI_TYPE sushi, NOTE_TYPE note, float length = 0)
    {
        switch(sushi)
        {
            case SUSHI_TYPE.L1:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        if(Inactive_L1_Short.transform.childCount == 0) SushiCreate(SUSHI_TYPE.L1, NOTE_TYPE.SHORT);
                        Inactive_L1_Short.transform.GetChild(0).gameObject.SetActive(true);
                        Inactive_L1_Short.transform.GetChild(0).position = L1Vector;
                        Inactive_L1_Short.transform.GetChild(0).rotation = LeftEuler;
                        Inactive_L1_Short.transform.GetChild(0).SetParent(Active_L1_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        if(Inactive_L1_Long.transform.childCount == 0) SushiCreate(SUSHI_TYPE.L1, NOTE_TYPE.LONG);
                        GameObject Obj = Inactive_L1_Long.transform.GetChild(0).gameObject;
                        Obj.SetActive(true);
                        Obj.transform.position = L1Vector;
                        Obj.transform.rotation = LeftEuler;
                        Obj.transform.GetChild(0).position = L1Vector;
                        Obj.transform.GetChild(2).position = L1Vector - new Vector3(length, 0, 0);
                        Obj.transform.GetChild(1).position = L1Vector - new Vector3(length * 0.45f, 0f, 0f);
                        Obj.transform.SetParent(Active_L1_Long.transform);
                        Obj.transform.GetChild(1).localScale = new Vector3(1f, 1f, length * 0.9f);
                        break;
                }
                break;
            case SUSHI_TYPE.L2:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        if(Inactive_L2_Short.transform.childCount == 0) SushiCreate(SUSHI_TYPE.L2, NOTE_TYPE.SHORT);
                        Inactive_L2_Short.transform.GetChild(0).gameObject.SetActive(true);
                        Inactive_L2_Short.transform.GetChild(0).position = L2Vector;
                        Inactive_L2_Short.transform.GetChild(0).rotation = LeftEuler;
                        Inactive_L2_Short.transform.GetChild(0).SetParent(Active_L2_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        if(Inactive_L2_Short.transform.childCount == 0) SushiCreate(SUSHI_TYPE.L2, NOTE_TYPE.LONG);
                        GameObject Obj = Inactive_L2_Long.transform.GetChild(0).gameObject;
                        Obj.SetActive(true);
                        Obj.transform.position = L2Vector;
                        Obj.transform.rotation = LeftEuler;
                        Obj.transform.GetChild(0).position = L2Vector;
                        Obj.transform.GetChild(2).position = L2Vector - new Vector3(length, 0, 0);
                        Obj.transform.GetChild(1).position = L2Vector - new Vector3(length * 0.45f, 0f, 0f);
                        Obj.transform.SetParent(Active_L2_Long.transform);
                        Obj.transform.GetChild(1).localScale = new Vector3(1, 1f, length * 0.9f);
                        break;
                }
                break;
            case SUSHI_TYPE.R1:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        if(Inactive_R1_Short.transform.childCount == 0) SushiCreate(SUSHI_TYPE.R1, NOTE_TYPE.SHORT);
                        Inactive_R1_Short.transform.GetChild(0).gameObject.SetActive(true);
                        Inactive_R1_Short.transform.GetChild(0).position = R1Vector;
                        Inactive_R1_Short.transform.GetChild(0).rotation = RightEuler;
                        Inactive_R1_Short.transform.GetChild(0).SetParent(Active_R1_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        if(Inactive_R1_Long.transform.childCount == 0) SushiCreate(SUSHI_TYPE.R1, NOTE_TYPE.LONG);
                        GameObject Obj = Inactive_R1_Long.transform.GetChild(0).gameObject;
                        Obj.SetActive(true);
                        Obj.transform.position = R1Vector;
                        Obj.transform.rotation = RightEuler;
                        Obj.transform.GetChild(0).position = R1Vector;
                        Obj.transform.GetChild(2).position = R1Vector + new Vector3(length, 0, 0);
                        Obj.transform.GetChild(1).position = R1Vector + new Vector3(length * 0.45f, 0f, 0f);
                        Obj.transform.SetParent(Active_R1_Long.transform);
                        Obj.transform.GetChild(1).localScale = new Vector3(1f, 1f, length * 0.9f);
                        break;
                }
                break;
            case SUSHI_TYPE.R2:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        if(Inactive_R2_Short.transform.childCount == 0) SushiCreate(SUSHI_TYPE.R2, NOTE_TYPE.SHORT);
                        Inactive_R2_Short.transform.GetChild(0).gameObject.SetActive(true);
                        Inactive_R2_Short.transform.GetChild(0).position = R2Vector;
                        Inactive_R2_Short.transform.GetChild(0).rotation = RightEuler;
                        Inactive_R2_Short.transform.GetChild(0).SetParent(Active_R2_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        if(Inactive_R2_Long.transform.childCount == 0) SushiCreate(SUSHI_TYPE.R2, NOTE_TYPE.LONG);
                        GameObject Obj = Inactive_R2_Long.transform.GetChild(0).gameObject;
                        Obj.SetActive(true);
                        Obj.transform.position = R2Vector;
                        Obj.transform.rotation = RightEuler;
                        Obj.transform.GetChild(0).position = R2Vector;
                        Obj.transform.GetChild(2).position = R2Vector + new Vector3(length, 0, 0);
                        Obj.transform.GetChild(1).position = R2Vector + new Vector3(length * 0.45f, 0f, 0f);
                        Obj.transform.SetParent(Active_R2_Long.transform);
                        Obj.transform.GetChild(1).localScale = new Vector3(1f, 1f, length * 0.9f);
                        break;
                }
                break;
        }
    }
    public void ReturnSushi(SUSHI_TYPE sushi, NOTE_TYPE note, GameObject obj)
    {
        obj.SetActive(false);
        switch(sushi)
        {
            case SUSHI_TYPE.L1:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        obj.transform.SetParent(Inactive_L1_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        obj.transform.SetParent(Inactive_L1_Long.transform);
                        break;
                }
                break;
            case SUSHI_TYPE.L2:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        obj.transform.SetParent(Inactive_L2_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        obj.transform.SetParent(Inactive_L2_Long.transform);
                        break;
                }
                break;
            case SUSHI_TYPE.R1:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        obj.transform.SetParent(Inactive_R1_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        obj.transform.SetParent(Inactive_R1_Long.transform);
                        break;
                }
                break;
            case SUSHI_TYPE.R2:
                switch(note)
                {
                    case NOTE_TYPE.SHORT:
                        obj.transform.SetParent(Inactive_R2_Short.transform);
                        break;
                    case NOTE_TYPE.LONG:
                        obj.transform.SetParent(Inactive_R2_Long.transform);
                        break;
                }
                break;
        }
    }
    public void ReturnAllSushi()
    {
        ReturnAll(Active_L1_Short.transform, SUSHI_TYPE.L1, NOTE_TYPE.SHORT);
        ReturnAll(Active_L2_Short.transform, SUSHI_TYPE.L2, NOTE_TYPE.SHORT);
        ReturnAll(Active_R1_Short.transform, SUSHI_TYPE.R1, NOTE_TYPE.SHORT);
        ReturnAll(Active_R2_Short.transform, SUSHI_TYPE.R2, NOTE_TYPE.SHORT);
        ReturnAll(Active_L1_Long.transform, SUSHI_TYPE.L1, NOTE_TYPE.LONG);
        ReturnAll(Active_L2_Long.transform, SUSHI_TYPE.L2, NOTE_TYPE.LONG);
        ReturnAll(Active_R1_Long.transform, SUSHI_TYPE.R1, NOTE_TYPE.LONG);
        ReturnAll(Active_R2_Long.transform, SUSHI_TYPE.R2, NOTE_TYPE.LONG);
    }
    public void ReturnAll(Transform Parent, SUSHI_TYPE type, NOTE_TYPE note)
    {
        while(Parent.childCount > 0)
        {
            GameObject obj = Parent.GetChild(0).gameObject;
            ReturnSushi(type, note, obj);
        }
    }
}
