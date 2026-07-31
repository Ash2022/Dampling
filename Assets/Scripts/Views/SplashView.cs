using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class SplashView : MonoBehaviour
{
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private Image progressBar;
    [SerializeField] private Button continueButton;

    public void UpdateProgress(float progress)
    {
        progress/=100f;

        progressBar.fillAmount = progress;
    }

    public void SetText(string text)
    {
        loadingText.text = text;
    }

    internal void EnableClickButton()
    {
        continueButton.enabled = true;
    }
}