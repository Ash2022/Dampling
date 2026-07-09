using DG.Tweening;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(SpriteRenderer))]
public class PuzzleFrameView : MonoBehaviour
{
    public enum Side { Up = 0, Right = 1, Down = 2, Left = 3 }

    [Header("Refs")]
    [SerializeField] private SpriteRenderer image;     // main tile image (auto-fills if null)

    [SerializeField] private SpriteRenderer frameL;
    [SerializeField] private SpriteRenderer frameR;
    [SerializeField] private SpriteRenderer frameT;
    [SerializeField] private SpriteRenderer frameB;

    [SerializeField] private SpriteMask capTL;
    [SerializeField] private SpriteMask capTR;
    [SerializeField] private SpriteMask capBR;
    [SerializeField] private SpriteMask capBL;

    [SerializeField] private SpriteRenderer capTLSR;
    [SerializeField] private SpriteRenderer capTRSR;
    [SerializeField] private SpriteRenderer capBRSR;
    [SerializeField] private SpriteRenderer capBLSR;



    // ---- internals ----
    private Dictionary<Side, SpriteRenderer> _sideToRenderer;
    private Dictionary<Side, Color> _baseColors;
  
   
    void Awake()
    {
                
        _sideToRenderer = new Dictionary<Side, SpriteRenderer>
        {
            { Side.Left,  frameL },
            { Side.Right, frameR },
            { Side.Up,    frameT },
            { Side.Down,  frameB },
        };

        _baseColors = new Dictionary<Side, Color>();
        foreach (var kv in _sideToRenderer)
        {
            if (kv.Value) _baseColors[kv.Key] = kv.Value.color;
        }
    }

 

   

    // Show/hide each frame side from a single truth passed in
    public void ApplyFrameMask(bool leftVisible, bool rightVisible, bool upVisible, bool downVisible)
    {
        if (_sideToRenderer.TryGetValue(Side.Left, out var l) && l) l.enabled = leftVisible;
        if (_sideToRenderer.TryGetValue(Side.Right, out var r) && r) r.enabled = rightVisible;
        if (_sideToRenderer.TryGetValue(Side.Up, out var t) && t) t.enabled = upVisible;
        if (_sideToRenderer.TryGetValue(Side.Down, out var b) && b) b.enabled = downVisible;

        // Corner caps: show only when BOTH adjacent sides are visible (outer corners)
        capTL.enabled = upVisible && leftVisible;
        capTR.enabled = upVisible && rightVisible;
        capBR.enabled = downVisible && rightVisible;
        capBL.enabled = downVisible && leftVisible;

        capTLSR.enabled = upVisible && leftVisible;
        capTRSR.enabled = upVisible && rightVisible;
        capBRSR.enabled = downVisible && rightVisible;
        capBLSR.enabled = downVisible && leftVisible;
    }

   

}
