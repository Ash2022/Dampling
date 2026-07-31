using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

public class DamplingObjectPool : MonoBehaviour
{
    public static DamplingObjectPool Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private GameObject unitPrefab;
    [SerializeField] private GameObject framePrefab;
    [SerializeField] private GameObject containerPrefab;
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private GameObject containerResolveEffectPrefab;


    private Queue<GameObject> unitPool = new Queue<GameObject>();
    private Queue<GameObject> framePool = new Queue<GameObject>();
    private Queue<GameObject> containerPool = new Queue<GameObject>();
    private Queue<GameObject> ballPool = new Queue<GameObject>();
    private Queue<GameObject> effectPool = new Queue<GameObject>();

    private Transform unitRoot;
    private Transform frameRoot;
    private Transform containerRoot;
    private Transform ballRoot;
    private Transform cellBlockerRoot;
    private Transform effectRoot;
    public Transform stageRoot;

    private void Awake()
    {
        Instance = this;
    }

    public async Task InitializePoolsAsync(Action<int> progress)
    {
        unitRoot = new GameObject("UnitPool_Root").transform;
        unitRoot.SetParent(transform);

        frameRoot = new GameObject("FramePool_Root").transform;
        frameRoot.SetParent(transform);
        
        containerRoot = new GameObject("ContainerPool_Root").transform;
        containerRoot.SetParent(transform);
        
        ballRoot = new GameObject("BallPool_Root").transform;
        ballRoot.SetParent(transform);

        cellBlockerRoot = new GameObject("EmptyUnitPool_Root").transform;
        cellBlockerRoot.SetParent(transform);

        effectRoot = new GameObject("EffectPool_Root").transform;
        effectRoot.SetParent(transform);

        await PrewarmPoolAsync(unitPrefab, 50, unitPool, unitRoot, 25);
        progress?.Invoke(30);
        await PrewarmPoolAsync(framePrefab, 150, framePool, frameRoot, 25);
        progress?.Invoke(40);
        await PrewarmPoolAsync(containerPrefab, 150, containerPool, containerRoot, 50);
        progress?.Invoke(50);
        await PrewarmPoolAsync(ballPrefab, 500, ballPool, ballRoot, 100);
        progress?.Invoke(60);
        await PrewarmPoolAsync(containerResolveEffectPrefab, 25, effectPool, effectRoot, 25);
        progress?.Invoke(70);
    }

    private async Task PrewarmPoolAsync(GameObject prefab, int count, Queue<GameObject> pool, Transform root, int objectsPerFrame)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject instance = Instantiate(prefab, root);
            instance.SetActive(false);
            pool.Enqueue(instance);

            if (i % objectsPerFrame == 0)
            {
                await Task.Yield(); 
            }
        }
    }

    public GameObject GetUnit(Vector3 position, Quaternion rotation, Transform parent)
    {
        if(unitPool.Count<=0)
            Debug.Log("Maybe Bug - missing Unit");

        GameObject obj = unitPool.Count > 0 ? unitPool.Dequeue() : Instantiate(unitPrefab);

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetFrame(Vector3 position, Quaternion rotation, Transform parent)
    {
        if(framePool.Count<=0)
            Debug.Log("Maybe Bug - missing Frame");

        GameObject obj = framePool.Count > 0 ? framePool.Dequeue() : Instantiate(framePrefab);

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetContainer(Vector3 position, Quaternion rotation, Transform parent)
    {
        if(containerPool.Count<=0)
            Debug.Log("Maybe Bug - missing Containers");

        GameObject obj = containerPool.Count > 0 ? containerPool.Dequeue() : Instantiate(containerPrefab);

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetBall(Vector3 position, Quaternion rotation)
    {
        if(ballPool.Count<=0)
            Debug.Log("Maybe Bug - missing Balls");

        GameObject obj = ballPool.Count > 0 ? ballPool.Dequeue() : Instantiate(ballPrefab);
        obj.transform.SetParent(stageRoot);
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetContainerResolveEffect(Vector3 position, Quaternion rotation)
    {
        if(effectPool.Count<=0)
            Debug.Log("Maybe Bug - missing Effect ");

        GameObject obj = effectPool.Count > 0 ? effectPool.Dequeue() : Instantiate(containerResolveEffectPrefab);

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public void ReturnUnit(GameObject unit)
    {
        unit.SetActive(false);
        unit.transform.SetParent(unitRoot);
        unitPool.Enqueue(unit);
    }

    public void ReturnFrame(GameObject frame)
    {
        frame.SetActive(false);
        frame.transform.SetParent(frameRoot);
        framePool.Enqueue(frame);
    }

    public void ReturnContainer(GameObject container)
    {
        container.SetActive(false);
        container.transform.SetParent(containerRoot);
        containerPool.Enqueue(container);
    }

    public void ReturnBall(GameObject ball)
    {
        ball.SetActive(false);
        ball.transform.SetParent(ballRoot);
        ballPool.Enqueue(ball);
    }

    public void ReturnContainerResolveEffect(GameObject effect)
    {
        if(effect==null)
            return;

        effect.SetActive(false);
        effect.transform.SetParent(effectRoot);
        effectPool.Enqueue(effect);
    }
    
}