using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

public class DamplingObjectPool : MonoBehaviour
{
    public static DamplingObjectPool Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private GameObject unitPrefab;
    [SerializeField] private GameObject containerPrefab;
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private GameObject EmptyUnitPrefab;
    

    private Queue<GameObject> unitPool = new Queue<GameObject>();
    private Queue<GameObject> containerPool = new Queue<GameObject>();
    private Queue<GameObject> ballPool = new Queue<GameObject>();
    private Queue<GameObject> emptyUnitPool = new Queue<GameObject>();

    private Transform unitRoot;
    private Transform containerRoot;
    private Transform ballRoot;
    private Transform emptyUnitRoot;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Staggers object instantiation across multiple execution blocks to guarantee zero main-thread hitching.
    /// </summary>
    public async Task InitializePoolsAsync()
    {
        unitRoot = new GameObject("UnitPool_Root").transform;
        unitRoot.SetParent(transform);
        
        containerRoot = new GameObject("ContainerPool_Root").transform;
        containerRoot.SetParent(transform);
        
        ballRoot = new GameObject("BallPool_Root").transform;
        ballRoot.SetParent(transform);

        emptyUnitRoot = new GameObject("EmptyUnitPool_Root").transform;
        emptyUnitRoot.SetParent(transform);

        // Pre-warm allocations sequentially, breaking across frames via task yields
        await PrewarmPoolAsync(unitPrefab, 100, unitPool, unitRoot, 25);
        await PrewarmPoolAsync(containerPrefab, 300, containerPool, containerRoot, 50);
        await PrewarmPoolAsync(ballPrefab, 1000, ballPool, ballRoot, 100);
        await PrewarmPoolAsync(EmptyUnitPrefab, 20, emptyUnitPool, emptyUnitRoot, 10);
    }

    private async Task PrewarmPoolAsync(GameObject prefab, int count, Queue<GameObject> pool, Transform root, int objectsPerFrame)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject instance = Instantiate(prefab, root);
            instance.SetActive(false);
            pool.Enqueue(instance);

            // Yield control back to the main engine loop once the frame allotment threshold is reached
            if (i % objectsPerFrame == 0)
            {
                await Task.Yield(); 
            }
        }
    }

    public GameObject GetUnit(Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject obj = unitPool.Count > 0 ? unitPool.Dequeue() : Instantiate(unitPrefab);
        obj.transform.SetParent(parent);
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetContainer(Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject obj = containerPool.Count > 0 ? containerPool.Dequeue() : Instantiate(containerPrefab);
        obj.transform.SetParent(parent);
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetBall(Vector3 position, Quaternion rotation)
    {
        GameObject obj = ballPool.Count > 0 ? ballPool.Dequeue() : Instantiate(ballPrefab);
        obj.transform.SetParent(null); 
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetEmptyUnit(Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject obj = emptyUnitPool.Count > 0 ? emptyUnitPool.Dequeue() : Instantiate(EmptyUnitPrefab);
        obj.transform.SetParent(parent);
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

    public void ReturnEmptyUnit(GameObject emptyUnit)
    {
        emptyUnit.SetActive(false);
        emptyUnit.transform.SetParent(emptyUnitRoot);
        emptyUnitPool.Enqueue(emptyUnit);
    }
}