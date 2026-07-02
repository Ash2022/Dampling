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

    private const int PATH_RESOLUTION = 200;
    private Vector3[] pathPoints;
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

        // Pre-calculate the path points for O(1) lookup
        pathPoints = new Vector3[PATH_RESOLUTION];
        for (int i = 0; i < PATH_RESOLUTION; i++)
        {
            float d = (perimeter / PATH_RESOLUTION) * i;
            pathPoints[i] = CalculatePositionAtDistance(d);
        }

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
            float distance = (currentOffset + (i * stepDistance)) % perimeter;
            if (distance < 0) distance += perimeter;

            // Find position in the pre-calculated path array
            float pathT = distance / perimeter;
            float floatIndex = pathT * PATH_RESOLUTION;
            int index1 = (int)floatIndex;
            int index2 = (index1 + 1) % PATH_RESOLUTION;
            float lerpT = floatIndex - index1;

            slots[i].position = Vector3.Lerp(pathPoints[index1], pathPoints[index2], lerpT);
        }
    }

    private Vector3 CalculatePositionAtDistance(float d)
    {
        if (d < straightLength)
            return Vector3.Lerp(leftCenter.position + upDir * radius, rightCenter.position + upDir * radius, d / straightLength);
        if (d < straightLength + curveLength)
            return rightCenter.position + ((upDir * Mathf.Cos((d - straightLength) / radius)) + (right * Mathf.Sin((d - straightLength) / radius))) * radius;
        if (d < (straightLength * 2f) + curveLength)
            return Vector3.Lerp(rightCenter.position - upDir * radius, leftCenter.position - upDir * radius, (d - (straightLength + curveLength)) / straightLength);
        return leftCenter.position + ((-upDir * Mathf.Cos((d - ((straightLength * 2f) + curveLength)) / radius)) - (right * Mathf.Sin((d - ((straightLength * 2f) + curveLength)) / radius))) * radius;
    }
}