using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class CharacterPopAnimationView : MonoBehaviour
{
    [SerializeField] private CanvasGroup mainCanvasGroup;
    [SerializeField] private RectTransform logo;
    [SerializeField] private List<RectTransform> characters;
    [SerializeField] private RectTransform boxFront;
    [SerializeField] private RectTransform boxBackground;

    private Dictionary<RectTransform, Vector2> originalPositions = new Dictionary<RectTransform, Vector2>();
    private Vector3 originalLogoScale;
   

    public void ResetState()
    {
        if(originalLogoScale == Vector3.zero)
            originalLogoScale = logo.localScale;

        if(originalPositions.Count == 0)
        {
            foreach (var character in characters)
                originalPositions[character] = character.anchoredPosition;
        }


        if (mainCanvasGroup != null)
        {
            mainCanvasGroup.alpha = 0f;
        }

        if (logo != null)
        {
            logo.gameObject.SetActive(false);
            logo.localScale = Vector3.zero;
        }

        for (int i = 0; i < characters.Count; i++)
        {
            characters[i].gameObject.SetActive(false);
            Vector2 targetPos = originalPositions[characters[i]];
            float yOffset = (i == 0) ? -180f : -100f;
            characters[i].anchoredPosition = targetPos + new Vector2(0f, yOffset);
            characters[i].localScale = new Vector3(1f, 0.2f, 1f);
        }
    }

    public void PlayAppearAnimation()
    {
        ResetState();

        Sequence sequence = DOTween.Sequence();

        if (mainCanvasGroup != null)
        {
            sequence.Append(mainCanvasGroup.DOFade(1f, 0.2f));
        }

        if (logo != null)
        {
            logo.gameObject.SetActive(true);
            sequence.Append(logo.DOScale(originalLogoScale * 1.15f, 0.3f).SetEase(Ease.OutBack));
            sequence.Append(logo.DOScale(originalLogoScale, 0.15f).SetEase(Ease.InOutSine));
        }

        if (characters.Count >= 4)
        {
            RectTransform char1 = characters[0];
            RectTransform char2 = characters[1];
            RectTransform char3 = characters[2];
            RectTransform char4 = characters[3];

            foreach (var character in characters)
            {  
                character.gameObject.SetActive(true);
            }

            SoundsManager.Instance.PlayLevelCompleteBG();

            Vector2 pos4 = originalPositions[char4];
            sequence.Append(char4.DOAnchorPosY(pos4.y, 0.3f).SetEase(Ease.OutBack).OnStart(()=>
            {
                SoundsManager.Instance.PlayLevelCompleteChars();
            }));
            sequence.Join(char4.DOScaleY(1f, 0.3f).SetEase(Ease.OutBack));

            Vector2 pos2 = originalPositions[char2];
            Vector2 pos3 = originalPositions[char3];

            sequence.Append(char2.DOAnchorPosY(pos2.y, 0.3f).SetEase(Ease.OutBack).OnStart(()=>
            {
                SoundsManager.Instance.PlayLevelCompleteChars();
            }));
            sequence.Join(char2.DOScaleY(1f, 0.3f).SetEase(Ease.OutBack));



            sequence.Join(char3.DOAnchorPosY(pos3.y, 0.3f).SetEase(Ease.OutBack).OnStart(()=>
            {
                SoundsManager.Instance.PlayLevelCompleteChars();
            }).SetDelay(0.1f));
            sequence.Join(char3.DOScaleY(1f, 0.3f).SetEase(Ease.OutBack));



            Vector2 pos1 = originalPositions[char1];
            sequence.Append(char1.DOAnchorPosY(pos1.y, 0.3f).SetEase(Ease.OutBack).OnStart(()=>
            {
                SoundsManager.Instance.PlayLevelCompleteChars();
            }));
            sequence.Join(char1.DOScaleY(1f, 0.3f).SetEase(Ease.OutBack));
        }

        sequence.Play();
    }
}