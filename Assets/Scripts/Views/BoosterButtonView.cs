using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class BoosterButtonView : MonoBehaviour
{
    public enum BoosterType { Magnet, Shuffle }

    [Header("Configuration")]
    [SerializeField] private BoosterType type;

    [Header("UI References")]
    [SerializeField] private Button actionButton;
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject counterBG;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private TextMeshProUGUI countText;

    [SerializeField] private RectTransform rect;

    public RectTransform Rect => rect;

    public BoosterType Type => type;


    public void Setup(bool isUnlocked, int count, Action<BoosterType> onClickCallback)
    {
        actionButton.onClick.RemoveAllListeners();
        
        lockedOverlay.SetActive(!isUnlocked);
        //iconImage.gameObject.SetActive(isUnlocked);
        countText.gameObject.SetActive(isUnlocked);
        counterBG.gameObject.SetActive(isUnlocked);

        if (isUnlocked)
        {
            countText.text = count.ToString();
            actionButton.onClick.AddListener(() => onClickCallback?.Invoke(type));
        }
    }
}