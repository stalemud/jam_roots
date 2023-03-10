using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Threading.Tasks;

public class Tentacle : Entity
{
    private static Tentacle _currentlySelected;

    public Vector2 EndPosition { get { return _endPosition; } }
    public Vector2 TipFacingDirection { get; private set; } = Vector2.up;
    public int length;

    [Header("Tentacle Settings")]
    [SerializeField] private Material _material;
    [SerializeField] private AnimationCurve _thicknessCurve;
    [SerializeField] private float _thickness;
    [SerializeField] private GameObject _handle;
    [SerializeField] private float _handleRadius = 0.5f;
    [SerializeField] private float _attackRange = 0.5f;
    [SerializeField] private float _movementSpeed = 1f;
    [SerializeField] private Sprite _burrowIcon;
    [SerializeField] private Image _healthBar;
    [SerializeField] private Canvas _canvas;
    [SerializeField] private AudioClip _moveSfx;

    private bool _isRooted;
    private TentacleExtension _extension;
    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private Vector2[] _currentVertices;
    private bool _grabbed = false;
    private Vector2Int _targetPos;
    private Vector2Int _startPosition;
    private Vector2Int _endPosition;
    private Coroutine _movementCoroutine;
    private Vector2Int _currentlyMovingTo;
    private MeshCollider _collider;
    private Material _materialInst;
    private float _sfxCooldown = 0.33f;

    public void Init() 
    {
        _startPosition = new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        _endPosition = _startPosition;
        _targetPos = _endPosition;
        _filter = gameObject.AddComponent<MeshFilter>();
        _renderer = gameObject.AddComponent<MeshRenderer>();
        _materialInst = new Material(_material);
        _renderer.material = _materialInst;
        _collider = gameObject.AddComponent<MeshCollider>();
        _currentlyMovingTo = _startPosition;
        _extension = GetComponentInChildren<TentacleExtension>();
    }

    public override ActionButton.Definition[] GetActionButtons()
    {
        if (_isRooted) return null;

        var buttons = new List<ActionButton.Definition>();
        
        var burrowCost = new ResourceClass[] { new ResourceClass("Biomass", 100) };
        buttons.Add(new ActionButton.Definition()
        {
            sprite = _burrowIcon,
            title = "Burrow",
            
            description = "Burrow into the ground, making the root permanently fixed in place, but sprouting two new roots.",
            resourceList = burrowCost,
            onClick = () =>
            {
                if (ResourceControl.Current.TrySpend(burrowCost))
                {
                    Burrow();
                    UnitInspectorUI.Current.Open(this);
                }
            }
        });
        

        foreach(var ext in Player.Extensions)
        {
            buttons.Add(new ActionButton.Definition()
            {
                sprite = ext.GetComponent<SpriteRenderer>().sprite,
                resourceList = ext.Cost,
                title = ext.Name,
                description = ext.Description,
                onClick = () => {
                    if (ResourceControl.Current.TrySpend(ext.Cost))
                    {
                        AddExtension(ext);
                    }
                }
            });
        }

        return buttons.ToArray();
    }

    public void Burrow()
    {
        _isRooted = true;
        var freeSpots = Pathfinding.GetTilesInRange(_endPosition, 3).Where(p => p.Position != _endPosition).ToArray();

        var tentacle = Player.Current.CreateTentacle(_endPosition, Player.Extensions.FirstOrDefault(e => e.Name == "Club"));
        tentacle.transform.SetParent(transform, true);
        tentacle.Move(freeSpots[Random.Range(0, freeSpots.Length)].Position);
        tentacle = Player.Current.CreateTentacle(_endPosition, Player.Extensions.FirstOrDefault(e => e.Name == "Club"));
        tentacle.transform.SetParent(transform, true);
        tentacle.Move(freeSpots[Random.Range(0, freeSpots.Length)].Position);

        Destroy(gameObject.GetComponentInChildren<TentacleExtension>().gameObject);
    }

    public void AddExtension(TentacleExtension extension)
    {
        if (_extension != null)
        {
            Destroy(_extension.gameObject);
        }

        var inst = Instantiate(extension, _endPosition.ToVector2(), Quaternion.identity);
        inst.transform.parent = transform;

        _extension = inst;
    }

    public void Move(Vector2Int position)
    {
        //Debug.Log("Move to " + position);
        if (_movementCoroutine != null)
            StopCoroutine(_movementCoroutine);
        //_entity.ClearBlockers();
        _movementCoroutine = StartCoroutine(MovementSequence(_endPosition, position));
    }

    IEnumerator FitTentacleToPath(Vector2Int targetPosition)
    {
        var pathTask = Pathfinding.GetPathAsync(_startPosition, targetPosition, length);

        while (!pathTask.IsCompleted)
        {
            yield return null;
        }

        var path = pathTask.Result;
        pathTask.Dispose();

        if (path == null)
        {
            yield break;
        }
        
        var last = path[0].Position;
        _currentVertices = new Vector2[path.Count];

        for (int i = 0; i < path.Count; i++)
        {
            Debug.DrawLine(new Vector2(last.x, last.y), new Vector2(path[i].Position.x, path[i].Position.y), Color.yellow, 3);
            last = path[i].Position;
            _currentVertices[i] = new Vector2(path[i].Position.x, path[i].Position.y);
        }

        pathTask.Dispose();
        _endPosition = path[path.Count - 1].Position;
        _extension.transform.position = _endPosition.ToVector2();

        if (path.Count > 1)
        {
            TipFacingDirection = (path[path.Count - 1].Position - path[path.Count - 2].Position).ToVector2().normalized;
        }

        GenerateMesh();
        //_entity.SetOccupiedTiles(path.Skip(2).ToArray());
    }

    IEnumerator MovementSequence(Vector2Int fromPosition, Vector2Int toPosition)
    {
        if (_sfxCooldown <= 0f)
        {
            AudioController.PlaySfx(_moveSfx, _endPosition, 1f, true);
            _sfxCooldown = _moveSfx.length * 0.9f;
        }

        _currentlyMovingTo = toPosition;
        var curPos = fromPosition;

        var pathTask = Pathfinding.GetPathAsync(fromPosition, toPosition, 10000);

        while (!pathTask.IsCompleted)
        {
            yield return null;
        }

        var path = pathTask.Result;
        pathTask.Dispose();

        if (path == null)
        {
            _movementCoroutine = null;
            yield break;
        }

        var last = path[0].Position;

        for (int i = 0; i < path.Count; i++)
        {
            Debug.DrawLine(new Vector2(last.x, last.y), new Vector2(path[i].Position.x, path[i].Position.y), Color.yellow, 4);
            last = path[i].Position;
        }


        while(path.Count > 0)
        {
            var nextPos = path[0].Position;
            path.RemoveAt(0);

            yield return StartCoroutine(FitTentacleToPath(nextPos));

            yield return new WaitForSeconds(1f / _movementSpeed);
        }

        if (!_grabbed)
            _handle.transform.position = new Vector3(_endPosition.x, _endPosition.y, 0);

        _movementCoroutine = null;
    }

    public bool TestHit(Vector2 position)
    {
        return (Vector2.Distance(position, _endPosition) < _handleRadius);
    }

    public void GenerateMesh()
    {
        if (_filter.mesh != null)
        {
            DestroyImmediate(_filter.mesh);
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        var mesh = new Mesh();
        var tpos = transform.position;

        var lastVert = new Vector3(_currentVertices[0].x, _currentVertices[0].y, 0);

        var connectingVert1 = lastVert;
        var connectingVert2 = lastVert;

        for (int i = 1; i < _currentVertices.Length; i++)
        {
            var curVert = new Vector3(_currentVertices[i].x, _currentVertices[i].y, 0);
            var fwd = (_currentVertices[i] - _currentVertices[i - 1]).normalized;
            var rightv2 = fwd.RotateVector(90);
            var right = new Vector3(rightv2.x, rightv2.y, 0);

            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(0, 1));
            uvs.Add(new Vector2(1, 1));

            var thickness = _thicknessCurve.Evaluate((float)i / _currentVertices.Length) * _thickness;

            
            var vert1 = i == 1 ? lastVert + (right * thickness) : connectingVert1;
            var vert2 = i == 1 ? lastVert - (right * thickness) : connectingVert2;
            var vert3 = curVert + (right * thickness);
            var vert4 = curVert - (right * thickness);

            connectingVert1 = vert3;
            connectingVert2 = vert4;

            vertices.Add(vert1 - tpos - (Vector3.forward * 10));
            vertices.Add(vert2 - tpos - (Vector3.forward * 10));
            vertices.Add(vert3 - tpos - (Vector3.forward * 10));
            vertices.Add(vert4 - tpos - (Vector3.forward * 10));

            lastVert = curVert;
        }

        for(int i = 0; i < vertices.Count; i+=4)
        {
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 1);

            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }

        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = tris.ToArray();
        _collider.sharedMesh = mesh;

        _filter.mesh = mesh;
    }

    private void Update() 
    {
        var mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;

        if (_sfxCooldown > 0f)
        {
            _sfxCooldown -= Time.deltaTime;
        }

        _healthBar.fillAmount = Health / MaxHealth;
        _canvas.transform.position = _endPosition.ToVector2();

        var dist = Vector3.Distance(mouseWorldPos, _handle.transform.position);

        //Debug.DrawLine(new Vector2(startPosition.x, startPosition.y), new Vector2(mouseWorldPos.x, mouseWorldPos.y), Color.red);

        if (!_isRooted && _currentlySelected == null && dist < _handleRadius)
        {
            _handle.transform.localScale = Vector3.one + (Vector3.one * (0.25f * Mathf.Sin(Time.time * 4f)));

            if (Input.GetMouseButtonDown(0))
            {
                _grabbed = true;
                _currentlySelected = this;
                World.Current.ShowPathingTiles(Pathfinding.GetTilesInRange(_startPosition, length));
                UnitInspectorUI.Current.Open(this);
                _handle.GetComponent<SpriteRenderer>().color = Color.green;
            }
        }
        else
        {
            _handle.transform.localScale = Vector3.one;
        }

        if(_grabbed)
        {
            _targetPos = new Vector2Int(Mathf.RoundToInt(mouseWorldPos.x), Mathf.RoundToInt(mouseWorldPos.y));

            _handle.transform.position = mouseWorldPos;

            if (!Input.GetMouseButton(0))
            {
                _handle.GetComponent<SpriteRenderer>().color = Color.white;
                _grabbed = false;
                _handle.transform.position = new Vector3(_targetPos.x, _targetPos.y, 0);
                _currentlySelected = null;
                World.Current.ShowPathingTiles(null);

                if (!World.Current.GetTile(_targetPos).Def.IsWalkable)
                {
                    _handle.transform.position = new Vector3(_endPosition.x, _endPosition.y, 0);
                }
            }

            if (_targetPos != _currentlyMovingTo)
            {
                Move(_targetPos);
            }
        }
        else
        {
            if (_movementCoroutine == null)
            {
                if (_isRooted)
                {
                    _handle.SetActive(false);
                }
                else
                {
                    _handle.transform.position = new Vector3(_endPosition.x, _endPosition.y, 0);
                }
            }
        }
    }
}
