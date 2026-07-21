using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class BeltGenerator : MonoBehaviour
{

    const float FAST_SPEED = 3.5f;
    const float BELT_SPEED = 1.75f;

    [Header("Visual Configurations")]
    [SerializeField] Color lightBrown;
    [SerializeField] Color darkBrown;

    private DG.Tweening.Sequence blinkSequence;

    public Transform leftCenter;
    public Transform rightCenter;
    public GameObject slotPrefab;
    public int slotCount = 28;
    public float radius = 2f;

    [Header("Movement Settings")]
    public float speed = BELT_SPEED;
    [Tooltip("1 for clockwise, -1 for counter-clockwise")]
    public int direction = 1;

    public Transform[] slots;
    private List<SlotView> slotViews = new List<SlotView>();

    [SerializeField] SpriteRenderer beltBlinker;

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
        if (beltActive)
            MoveBelt();
    }

    public void InitializeBelt(int levelSlotCount)
    {
        slotCount = levelSlotCount;
        GenerateBelt();
    }

    public void StartBeltMovement()
    {
        speed = BELT_SPEED;
        beltActive = true;
    }

    public void ResetSlots()
    {
        foreach (SlotView slot in slotViews)
            slot.Release();

        EvaluateBlinkStatus();
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

            SlotView view = newSlot.GetComponent<SlotView>();
            slotViews.Add(view);

            // Grouping by 3: alternate colors every 3 slots
            int groupIndex = i / 3;
            view.SR.color = (groupIndex % 2 == 0) ? lightBrown : darkBrown;
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

            float pathT = distance / perimeter;
            float floatIndex = pathT * PATH_RESOLUTION;
            int index1 = (int)floatIndex % PATH_RESOLUTION;
            int index2 = (index1 + 1) % PATH_RESOLUTION;
            float lerpT = floatIndex - (int)floatIndex;

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

    internal bool AllSlotsFull()
    {
        foreach (SlotView slot in slotViews)
            if (slot.IsOccupied == false)
                return false;

        return true;
    }


    public List<int> GetBeltsColors()
    {
        List<int> colors = new List<int>();
        foreach (SlotView slot in slotViews)
            if (slot.IsOccupied)
                colors.Add(slot.OccupyingBall.ColorIndex);

        return colors;
    }

    internal void StopBeltMovement()
    {
        beltActive = false;
    }

    public void ExtractBallsByColor(int targetColorIndex, int amountToRemove)
    {
        int removedCount = 0;

        // Iterate through your belt slots (Assuming you have a list or array of slots)
        // Iterate backwards so removing items doesn't mess up the loop index if it shifts
        for (int i = slotViews.Count - 1; i >= 0; i--)
        {
            var slot = slotViews[i];

            if (slot.IsOccupied && slot.OccupyingBall.ColorIndex == targetColorIndex)
            {
                // 1. Return the visual ball to the object pool
                DamplingObjectPool.Instance.ReturnBall(slot.OccupyingBall.gameObject);

                // 2. Clear the slot data so it accepts new balls
                slot.Release();                
                
                removedCount++;
                if (removedCount >= amountToRemove)
                    break;

            }
        }

        EvaluateBlinkStatus();
    }

    internal void ResumeBelt()
    {
        beltActive = true;
    }

    private void EvaluateBlinkStatus()
    {
        int emptyCount = 0;
        foreach (SlotView slot in slotViews)
        {
            if (!slot.IsOccupied)
                emptyCount++;
        }

        if (emptyCount <= 3 && emptyCount > 0)
        {
            StartBlinking();
        }
        else
        {
            StopBlinking();
        }
    }

    private void StartBlinking()
    {
        if (blinkSequence != null && blinkSequence.IsActive()) return;

        blinkSequence = DG.Tweening.DOTween.Sequence();
        blinkSequence.Append(beltBlinker.DOColor(new Color(1f, 0f, 0f, 1f), 0.4f).SetEase(DG.Tweening.Ease.InOutSine));
        blinkSequence.Append(beltBlinker.DOColor(new Color(1f, 1f, 1f, 0f), 0.4f).SetEase(DG.Tweening.Ease.InOutSine));
        blinkSequence.SetLoops(-1, DG.Tweening.LoopType.Restart);
    }

    private void StopBlinking()
    {
        if (blinkSequence != null)
        {
            blinkSequence.Kill();
            blinkSequence = null;
        }
        beltBlinker.color = new Color(1f, 1f, 1f, 0f);
    }

    internal void CheckBeltFullness()
    {
        EvaluateBlinkStatus();
    }

    internal void IncreaseBeltSpeed()
    {
        speed = FAST_SPEED;
    }
}