using DG.Tweening;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SocialPlatforms.Impl;

public class UIManager : MonoBehaviour
{
    const int BUTTON_TUTORIAL = 11;
    const int BONUS_BUBBLE = 5;

    [SerializeField] Canvas canvas;
    [SerializeField] private TMP_Text levelText;

    [SerializeField] Canvas balanceSortingGroup;
    [SerializeField] RectTransform balanceRect;

    [SerializeField] private TMP_Text balanceText;

    [SerializeField] RectTransform inGameUIHolder;

    [SerializeField] List<Sprite> tutorialImages = new List<Sprite>();
    [SerializeField] Sprite hardLevelImage;

    [SerializeField] TutorialImageView tutorialImageView;



    int currDisplayBalance;


    [SerializeField] RectTransform tutorialHand;
    Sequence handSequence;


    public void InitLevel(int levelIndex, int balance, int unlockedIndex, bool isHardLevel, bool showTutorial)
    {
        balanceSortingGroup.sortingLayerName = "Default";
        currDisplayBalance = balance;
        AddToBalanceVisual(0);
        levelText.text = "LEVEL " + (levelIndex + 1).ToString();


        if (levelIndex == 0)
        {
            //get the first container position 
            UnitView unitView = GameManager.Instance.GetUnitViewAtPosition(2, 0);
            Vector3 containerPosition = unitView.transform.position;
            ShowTutorialHand(containerPosition, 0);
        }
        else
            HideTutorialHand();


        ShowTutorialImage(showTutorial, unlockedIndex, isHardLevel);

    }


    

    public Vector2 WorldToAnchoredPos(Vector3 worldPos, RectTransform container)
    {
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(Camera.main, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(container, screen, null, out var local);
        return local;
    }

    public void AddToBalanceVisual(int amount)
    {
        currDisplayBalance += amount;

        if (currDisplayBalance > ModelManager.Instance.GetBalance())
            currDisplayBalance = ModelManager.Instance.GetBalance();

        balanceText.text = currDisplayBalance.ToString();

    }

    public Vector3 GetBalancePosition()
    {
        return balanceRect.position;
    }

    internal void MoveBalanceUpOnSort()
    {
        balanceSortingGroup.overrideSorting = true;
        balanceSortingGroup.sortingOrder = 10;
    }

    public void SetBalanceToModel()
    {
        currDisplayBalance = ModelManager.Instance.GetBalance();
        AddToBalanceVisual(0);
    }



    public void ShowTutorialHand(Vector3 position, int index)
    {
        if (handSequence != null)
            handSequence.Kill();

        tutorialHand.localScale = Vector3.one;

        tutorialHand.localPosition = WorldToAnchoredPos(position, inGameUIHolder) + new Vector2(50, -50);

        tutorialHand.gameObject.SetActive(true);

        handSequence = DOTween.Sequence();

        handSequence.Append(tutorialHand.DOScale(0.8f, .8f).SetEase(Ease.InOutSine).SetLoops(100, LoopType.Yoyo));

        handSequence.Play();

    }



    internal void HideTutorialHand(bool hideAlsoText = false)
    {
        if (handSequence != null)
            handSequence.Kill();

        tutorialHand.gameObject.SetActive(false);


    }

    public void SetBalanceToModelAnimate()
    {
        DOVirtual.Int(currDisplayBalance, ModelManager.Instance.GetBalance(), 1, (balanceValue) =>
        {
            balanceText.text = balanceValue.ToString();
        });
    }

    internal void GameOver()
    {
        Debug.Log("UIManage GameOver");
    }


    public void ShowTutorialImage(bool show, int imageIndex, bool hardLevel)
    {
        if (show)
        {
            //levelText.text = "";

            if (imageIndex > 0)
            {
                Sprite auxImage = null;

                tutorialImageView.ShowTutorial(tutorialImages[imageIndex - 1], auxImage);
            }

            if (hardLevel)
            {
                Sprite auxImage = null;

                tutorialImageView.ShowTutorial(hardLevelImage, auxImage);
            }
        }
        else
        {
            //hide
            tutorialImageView.HideTutorial();
        }
    }

    public void HideTutorial()
    {
        tutorialImageView.HideTutorial();
    }

}
