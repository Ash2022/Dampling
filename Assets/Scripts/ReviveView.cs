using System;
using TMPro;
using UnityEngine;

public class ReviveView : MonoBehaviour
{

    Action<bool> answerBack;
    [SerializeField] TMP_Text reviveCost;

    public void ShowRevive(Action<bool> action, int cost)
    {
        answerBack = action;
        gameObject.SetActive(true);  
        reviveCost.text = "RECOVER\n<size=50><Sprite=0>"+cost;  
    }

    public void ButtonYesClicked()
    {
        answerBack?.Invoke(true);
        gameObject.SetActive(false);
    }

    public void ButtonNoClicked()
    {
        answerBack?.Invoke(false);
        gameObject.SetActive(false);
    }
}
