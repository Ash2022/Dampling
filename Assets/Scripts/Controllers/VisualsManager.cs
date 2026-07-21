using System;
using System.Collections.Generic;
using UnityEngine;

public class VisualsManager : MonoBehaviour
{
    public static VisualsManager Instance { get; private set; }

    [SerializeField] List<Sprite> unlockBGs = new List<Sprite>();
    [SerializeField] List<Sprite> unlockFills = new List<Sprite>();

    [SerializeField] List<Sprite> ballSprites;
    [SerializeField] List<Sprite> containerSprites;
    [SerializeField] List<Sprite> unitSprites;
    [SerializeField] List<Sprite> unitLidsSprites;

    [SerializeField] List<Sprite> pipeCounterSprites;

    
    [SerializeField] Sprite unitHidden;
    [SerializeField] Sprite unitLidHidden;

    [SerializeField] Sprite containerHidden;

    [SerializeField] Sprite Pipe;
    

    private void Awake()
    {
        Instance = this;
    }


    public Sprite GetBallSprite(int index)
    {
        int targetedIndex = index % ballSprites.Count;
        return ballSprites[targetedIndex];
    }

    public Sprite GetContainerSprite(int index)
    {
        if(index==-1)
            return containerHidden;

        int targetedIndex = index % containerSprites.Count;
        return containerSprites[targetedIndex];
    }

    public Sprite GetUnitSprite(int index)
    {
        if(index==-1)
            return unitHidden;


        int targetedIndex = index % unitSprites.Count;
        return unitSprites[targetedIndex];
    }

    public Sprite GetUnitLidSprite(int index)
    {
        if(index==-1)
            return unitLidHidden;

        int targetedIndex = index % unitLidsSprites.Count;
        return unitLidsSprites[targetedIndex];
    }

    internal Sprite GetPipeSprite()
    {
        return Pipe;
    }

    public Sprite GetUnlockImage(int index, bool BGImage)
    {
        if (BGImage)
            return unlockBGs[index];
        else
            return unlockFills[index];
    }

    internal Sprite GetPipeCounterSprite(int v)
    {
        return pipeCounterSprites[v];
    }
}
