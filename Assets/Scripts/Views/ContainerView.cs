using UnityEngine;
using System.Linq;
using static GameLevelSchema;

public class ContainerView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    public void Initialize(ContainerData containerData)
    {
        string colorId = containerData.ColorId;
        spriteRenderer.color = DamplingGameUtils.GetColorById(colorId);
    }
}