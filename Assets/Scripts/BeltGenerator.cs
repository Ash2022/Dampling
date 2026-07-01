using UnityEngine;

public class BeltGenerator : MonoBehaviour
{
    public Transform leftCenter;
    public Transform rightCenter;
    public GameObject slotPrefab;
    public int slotCount = 28;
    public float radius = 2f;
    
    [Header("Movement Settings")]
    public float speed = 2f;
    [Tooltip("1 for clockwise, -1 for counter-clockwise")]
    public int direction = 1; 

    public Transform[] slots;
    private float perimeter;
    private float straightLength;
    private float curveLength;
    private Vector3 right;
    private Vector3 upDir;
    private float currentOffset;

    bool beltActive = false;

    void Update()
    {
        if(beltActive)
            MoveBelt();
    }

    public void InitializeBelt(int levelSlotCount)
    {
        slotCount = levelSlotCount;
        GenerateBelt();        
    }

    public void StartBeltMovement()
    {
        beltActive = true;
    }

    void GenerateBelt()
    {
        slots = new Transform[slotCount];
        
        Vector3 centerDir = rightCenter.position - leftCenter.position;
        straightLength = centerDir.magnitude;
        right = centerDir.normalized;
        upDir = Vector3.Cross(Vector3.forward, right).normalized; 

        curveLength = Mathf.PI * radius;
        perimeter = (straightLength * 2f) + (curveLength * 2f);

        for (int i = 0; i < slotCount; i++)
        {
            GameObject newSlot = Instantiate(slotPrefab, Vector3.zero, Quaternion.identity, transform);
            slots[i] = newSlot.transform;
        }

        UpdateSlotPositions();
    }

    void MoveBelt()
    {
        currentOffset += speed * direction * Time.deltaTime;
        
        // Keep the offset within bounds [0, perimeter]
        if (currentOffset < 0) currentOffset += perimeter;
        if (currentOffset > perimeter) currentOffset -= perimeter;

        UpdateSlotPositions();
    }

    void UpdateSlotPositions()
    {
        float stepDistance = perimeter / slotCount;

        for (int i = 0; i < slotCount; i++)
        {
            float d = (currentOffset + (i * stepDistance)) % perimeter;
            if (d < 0) d += perimeter;

            Vector3 pos = Vector3.zero;

            if (d < straightLength)
            {
                float t = d / straightLength;
                Vector3 start = leftCenter.position + upDir * radius;
                Vector3 end = rightCenter.position + upDir * radius;
                pos = Vector3.Lerp(start, end, t);
            }
            else if (d < straightLength + curveLength)
            {
                float distInCurve = d - straightLength;
                float angle = distInCurve / radius; 
                Vector3 dir = (upDir * Mathf.Cos(angle)) + (right * Mathf.Sin(angle));
                pos = rightCenter.position + dir * radius;
            }
            else if (d < (straightLength * 2f) + curveLength)
            {
                float distInStraight = d - (straightLength + curveLength);
                float t = distInStraight / straightLength;
                Vector3 start = rightCenter.position - upDir * radius;
                Vector3 end = leftCenter.position - upDir * radius;
                pos = Vector3.Lerp(start, end, t);
            }
            else
            {
                float distInCurve = d - ((straightLength * 2f) + curveLength);
                float angle = distInCurve / radius;
                Vector3 dir = (-upDir * Mathf.Cos(angle)) - (right * Mathf.Sin(angle));
                pos = leftCenter.position + dir * radius;
            }

            slots[i].position = pos;
        }
    }
}