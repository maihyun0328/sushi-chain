using System.Collections.Generic;
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

    // Update is called once per frame
    void Update()
    {
        
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
                            Instantiate(L1_Short_Prefab, Inactive_L1_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(L1_Long_Prefab, Inactive_L1_Long.transform).SetActive(false);
                            break;
                    }
                    break;
                case SUSHI_TYPE.L2:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(L2_Short_Prefab, Inactive_L2_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(L2_Long_Prefab, Inactive_L2_Long.transform).SetActive(false);
                            break;
                    }
                    break;
                case SUSHI_TYPE.R1:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(R1_Short_Prefab, Inactive_R1_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(R1_Long_Prefab, Inactive_R1_Long.transform).SetActive(false);
                            break;
                    }
                    break;
                case SUSHI_TYPE.R2:
                    switch(note)
                    {
                        case NOTE_TYPE.SHORT:
                            Instantiate(R2_Short_Prefab, Inactive_R2_Short.transform).SetActive(false);
                            break;
                        case NOTE_TYPE.LONG:
                            Instantiate(R2_Long_Prefab, Inactive_R2_Long.transform).SetActive(false);
                            break;
                    }
                    break;
            }
        }
    }
}
