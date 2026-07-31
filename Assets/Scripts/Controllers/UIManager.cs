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

    [SerializeField]SpriteRenderer FrameBG; 

    [SerializeField] Sprite hardLevelImage;

    [SerializeField] TutorialImageView tutorialImageView;

    [SerializeField] GameObject skipButton;

    int currDisplayBalance;


    [SerializeField] RectTransform tutorialHand;
    Sequence handSequence;


    public void InitLevel(int levelIndex, int balance, int unlockedIndex, bool isHardLevel, bool showTutorial)
    {
        ShowHideSkipButton(false);
        //balanceSortingGroup.sortingLayerName = "Default";
        currDisplayBalance = balance;
        AddToBalanceVisual(0);
        levelText.text = "LEVEL " + (levelIndex + 1).ToString();

        FrameBG.sprite = VisualsManager.Instance.GetBgSprite((levelIndex/10)%5);


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
        RectTransformUtility.ScreenPointToLocalPointInRectangle(container, screen, Camera.main, out var local);
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
        //Debug.Log("UIManage GameOver");
    }


    public void ShowTutorialImage(bool show, int imageIndex, bool hardLevel)
    {
        if (show)
        {
            //levelText.text = "";

            if (imageIndex > -1)
            {
                Sprite auxImage = null;

                ModelManager.UnlockTypes unlockType = (ModelManager.UnlockTypes)(imageIndex);

                Sprite unlockedImageSprite = VisualsManager.Instance.GetUnlockImage(imageIndex);

                tutorialImageView.ShowTutorial(unlockedImageSprite,
                auxImage, GetTitleText(unlockType), GetBodyText(unlockType));
            }

            if (hardLevel)
            {
                Sprite auxImage = null;

                tutorialImageView.ShowHardLevelIndication(hardLevelImage);
            }
        }
        else
        {
            //hide
            tutorialImageView.HideTutorial();
        }
    }

    private string GetTitleText(ModelManager.UnlockTypes unlockType)
    {
        switch (unlockType)
        {
            case ModelManager.UnlockTypes.HIDDEN:
                return "Hidden Unit Unlocked";
            case ModelManager.UnlockTypes.HIDDEN_CONTAINER:
                return "Hidden Container Unlocked";
            case ModelManager.UnlockTypes.ICE:
                return "Ice Unit Unlocked";
            case ModelManager.UnlockTypes.PIPE:
                return "Pipe Unit Unlocked";
            case ModelManager.UnlockTypes.LINK:
                return "Linked Units Unlocked";
            case ModelManager.UnlockTypes.LOCK_KEY:
                return "Lock And Key Unlocked";
            case ModelManager.UnlockTypes.MAGNET:
                return "Magnet Booster Unlocked";
            case ModelManager.UnlockTypes.SHUFFLE:
                return "Shuffle Booster Unlocked";
            case ModelManager.UnlockTypes.COVER_MAP:
                return "Map Unlocked";
            default:
                return "";
        }
    }

    private string GetBodyText(ModelManager.UnlockTypes unlockType)
    {
        switch (unlockType)
        {
            case ModelManager.UnlockTypes.HIDDEN:
                return "Units can now start <color=blue>Hidden";
            case ModelManager.UnlockTypes.HIDDEN_CONTAINER:
                return "Container can now start <color=blue>Hidden";
            case ModelManager.UnlockTypes.ICE:
                return "Play a unit next to this, to break the <color=blue>Ice";
            case ModelManager.UnlockTypes.PIPE:
                return "<color=blue>Pipe</color> holds a few units inside";
            case ModelManager.UnlockTypes.LINK:
                return "<color=blue>Linked</color> units both play together";
            case ModelManager.UnlockTypes.LOCK_KEY:
                return "Grab the <color=blue>Key</color> to open the <color=blue>Lock";
            case ModelManager.UnlockTypes.MAGNET:
                return "Pick any unit to <color=blue>Magnet</color> its contents";
            case ModelManager.UnlockTypes.SHUFFLE:
                return "<color=blue>Shuffle</color> the first 2 rows of containers";
            case ModelManager.UnlockTypes.COVER_MAP:
                return "Play units to remove the <color=blue>Map";
            default:
                return "";
        }
    }

    public void HideTutorial()
    {
        tutorialImageView.HideTutorial();
    }


    public void ShowHideSkipButton(bool showButton)
    {
        skipButton.SetActive(showButton);
    }

    //called from the scene
    public void SkipButtonClicked()
    {
        GameManager.Instance.SkipClicked();
        ShowHideSkipButton(false);
    }

}
