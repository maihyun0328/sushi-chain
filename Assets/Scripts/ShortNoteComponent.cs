using UnityEngine;

public class ShortNoteComponent : MonoBehaviour, INote
{
    [Header("Note")]
    [SerializeField] NOTE_DIRECT dir;
    [SerializeField] Transform head;
    [SerializeField] Transform tail;
    [SerializeField] SUSHI_TYPE type;

    [Header("Setting")]
    float speed;
    [SerializeField] float judgeX;
    [SerializeField] float judgeRange;
    [SerializeField] float FailRange;

    void Awake()
    {
        
    }
    void OnEnable()
    {
        speed = JsonManager.i.speed;
    }
    void Start()
    {
        
    }
    void Update()
    {
        Move();
        MissCheck();
    }

    void Move()
    {
        switch(dir)
        {
            case NOTE_DIRECT.RIGHT:
                transform.position += Vector3.left * speed * Time.deltaTime;
                break;
            case NOTE_DIRECT.LEFT:
                transform.position += Vector3.right * speed * Time.deltaTime;
                break;
        }
    }

    public void NoteCheck(KeyCode key)
    {
        if (IsInJudgeRange(transform.position.x))
        {
            Complete();
            return;
        }
    }
    void MissCheck()
    {
        switch(dir)
        {
            case NOTE_DIRECT.RIGHT:
                // 노트가 판정선을 지나쳤는데 입력하지 않음
                if (transform.position.x < judgeX - FailRange)
                {
                    Fail();
                }
                break;
            case NOTE_DIRECT.LEFT:
                // 노트가 판정선을 지나쳤는데 입력하지 않음
                if (transform.position.x > judgeX + FailRange)
                {
                    Fail();
                }
                break;
        }
    }
    public float GetJudgeX()
    {
        return head.position.x;
    }
    bool IsInJudgeRange(float x)
    {
        return Mathf.Abs(x - judgeX) <= judgeRange;
    }

    void Complete()
    {
        VFXManager.i.SushiVFX(dir);
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.SHORT, gameObject);
    }

    void Fail()
    {
        PointManager.i.Miss += 1;
        SushiPoolComponent.i.ReturnSushi(type, NOTE_TYPE.SHORT, gameObject);
    }
}