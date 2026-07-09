using System;
using UnityEngine;

public class FrameView : MonoBehaviour
{
    // --- OFFSET CONFIGURATION ---
    // Change this single value to adjust how far the inner curves project into the path.
    private const float AUX_OFFSET = 0.935f;

    [Header("Main Quadrants")]
    [SerializeField] SpriteRenderer TL;
    [SerializeField] SpriteRenderer TR;
    [SerializeField] SpriteRenderer BL;
    [SerializeField] SpriteRenderer BR;

    [Header("Auxiliary Quadrants (For Inner Curves)")]
    [SerializeField] SpriteRenderer TL_Aux;
    [SerializeField] SpriteRenderer TR_Aux;
    [SerializeField] SpriteRenderer BL_Aux;
    [SerializeField] SpriteRenderer BR_Aux;

    [Header("Sprites")]
    [SerializeField] Sprite straight;
    [SerializeField] Sprite corner;
    [SerializeField] Sprite innerCorner;
    [SerializeField] Sprite solidCover;

    public void ApplyFrameMask(bool left, bool right, bool up, bool down, bool upLeft, bool upRight, bool downLeft, bool downRight)
    {
        // Pass the 2 adjacent sides, the 1 diagonal, base rotation, renderers, AND the X/Y direction for the Aux offset
        EvaluateQuadrant(up, left, upLeft, 0f, TL, TL_Aux, new Vector2(-1f, 1f));         // Top-Left (Negative X, Positive Y)
        EvaluateQuadrant(right, up, upRight, -90f, TR, TR_Aux, new Vector2(1f, 1f));      // Top-Right (Positive X, Positive Y)
        EvaluateQuadrant(down, right, downRight, -180f, BR, BR_Aux, new Vector2(1f, -1f));// Bottom-Right (Positive X, Negative Y)
        EvaluateQuadrant(left, down, downLeft, 90f, BL, BL_Aux, new Vector2(-1f, -1f));   // Bottom-Left (Negative X, Negative Y)
    }

    private void EvaluateQuadrant(bool side1Blocked, bool side2Blocked, bool diagBlocked, float baseRot, SpriteRenderer mainSr, SpriteRenderer auxSr, Vector2 auxDir)
    {
        if (mainSr == null || auxSr == null) return;

        // Reset the Aux sprite by default
        auxSr.sprite = null;

        // RULE 1: Outer Corner
        if (!side1Blocked && !side2Blocked)
        {
            mainSr.sprite = corner;
            mainSr.transform.localRotation = Quaternion.Euler(0, 0, baseRot);
        }
        // RULE 2 & 3: Straight Lines
        else if (!side1Blocked && side2Blocked)
        {
            mainSr.sprite = straight;
            mainSr.transform.localRotation = Quaternion.Euler(0, 0, baseRot);
        }
        else if (side1Blocked && !side2Blocked)
        {
            mainSr.sprite = straight;
            mainSr.transform.localRotation = Quaternion.Euler(0, 0, baseRot + 90f);
        }
        // RULE 4: THE AUX FILLET (Inner Corner)
        else if (side1Blocked && side2Blocked && !diagBlocked)
        {
            // 1. Solid background for the main quadrant
            mainSr.sprite = solidCover;
            mainSr.transform.localRotation = Quaternion.identity;

            // 2. Setup the Aux Renderer
            auxSr.sprite = innerCorner;
            auxSr.transform.localRotation = Quaternion.Euler(0, 0, baseRot + 180f);

            // 3. Automatically snap the Aux Renderer to the exact offset position
            auxSr.transform.localPosition = new Vector3(auxDir.x * AUX_OFFSET, auxDir.y * AUX_OFFSET, 0f);
        }
        // RULE 5: Deep inside the wall
        else
        {
            mainSr.sprite = solidCover;
            mainSr.transform.localRotation = Quaternion.identity;
        }
    }

    public void ApplyTopRowOverride(bool pathLeft, bool pathRight)
    {
        // 1. Clear any Aux projections on the top edge so they don't poke out weirdly
        if (TL_Aux != null) TL_Aux.sprite = null;
        if (TR_Aux != null) TR_Aux.sprite = null;

        // 2. Cap the Top-Left Quadrant
        if (pathLeft)
        {
            TL.sprite = corner;
            TL.transform.localRotation = Quaternion.Euler(0, 0, 0f);
        }
        else
        {
            TL.sprite = straight;
            TL.transform.localRotation = Quaternion.Euler(0, 0, 0f); // Horizontal line
        }

        // 3. Cap the Top-Right Quadrant
        if (pathRight)
        {
            TR.sprite = corner;
            TR.transform.localRotation = Quaternion.Euler(0, 0, -90f);
        }
        else
        {
            TR.sprite = straight;
            TR.transform.localRotation = Quaternion.Euler(0, 0, 0f); // Horizontal line
        }
    }
}