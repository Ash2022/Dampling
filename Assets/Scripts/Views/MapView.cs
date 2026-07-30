using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;

public class MapView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer mapCounter;
    [SerializeField] private SpriteRenderer mapSpriteRenderer;

    List<UnitView> myMappedUnits = new List<UnitView>();


    public void Initialize(int startingCounter, int unitCount, bool isHorizontal,List<UnitView> mappedUnits)
    {
        myMappedUnits = mappedUnits;

        mapCounter.sprite = VisualsManager.Instance.GetPipeCounterSprite(startingCounter);

        mapSpriteRenderer.size = Vector3.one*1.1f;
        
        if (unitCount == 2)
        {
            mapSpriteRenderer.size = isHorizontal ? new Vector3(2f, 1f, 1f) : new Vector3(1f, 2f, 1f);
        }
        else if (unitCount == 4)
        {
            mapSpriteRenderer.size = new Vector3(2f, 2f, 1f);
        }
    }

    public void UpdateCounter(int newCounter)
    {
        mapCounter.sprite = VisualsManager.Instance.GetPipeCounterSprite(newCounter);
        transform.DOPunchScale(Vector3.one * 0.1f, 0.2f, 5, 1f);
    }

    public void PlayDestroyAnimation(System.Action onComplete)
    {
        transform.DOScale(Vector3.zero, 0.3f).SetEase(Ease.InBack).OnComplete(() =>
        {
            foreach (UnitView unitView in myMappedUnits)
                unitView.MapRemoved();
            onComplete?.Invoke();
            Destroy(gameObject);
        });
    }
}