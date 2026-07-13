using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReviveView : MonoBehaviour
{

    Action<bool> answerBack;

    public void ShowRevive(Action<bool> action)
    {
        answerBack = action;
        gameObject.SetActive(true);    
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
