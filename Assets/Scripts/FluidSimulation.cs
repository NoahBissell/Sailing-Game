using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

public class FluidSimulation : MonoBehaviour
{
    private static readonly int ParticlesID = Shader.PropertyToID("particles");
    private static readonly int NumParticlesID = Shader.PropertyToID("num_particles");
    private static readonly int ResultID = Shader.PropertyToID("result");
    private static readonly int PullPositionID = Shader.PropertyToID("pull_position");

    private static readonly int2[] Directions = 
    { new(0, 0), new(1, 0), new(1, 1), 
        new(0, 1), new(-1, 1), new(-1, 0), 
        new(-1, -1), new(0, -1), new(1, -1) 
    };
    private static readonly float2[] VectorComponents = {new(1, 0), new(0, 1)};
    private static readonly float2[] ComponentReversals = {new(-1, 1), new(1, -1)};
    
    public int numParticles;
    public float totalMass;
    public float bounciness = .3f;
    public float circleFriction = 1f;
    public float viscosity = .3f;
    public float circleSurfaceStrength = 5000f;
    public float2 gravity;
    // public float2 boundsOffset;
    public float2 boundsSize;
    public float boundsRotation;
    public float simTimeStep = .013333f;
    public float minDt = 1 / 120f;
    public float startRadius;

    public float influenceRadius;
    public float pressureMultiplier;
    public float targetDensity;
    public float pullRadius;
    public float pullStrength;
    public float sampleRadius;

    public Transform[] rectangles;
    public Rigidbody2D[] circles;

    public Material fluidMaterial;
    public ComputeShader fluidCompute;
    public Vector2Int textureSize;
    public Vector2 sampleArea;
    
    private RenderTexture _fluidSampleTexture;
    
    private ComputeBuffer _particleBuffer;
    private ComputeBuffer _rectangleBuffer;
    private ComputeBuffer _circleInfoBuffer;
    private ComputeBuffer _circleStateBuffer;
    private ComputeBuffer _collisionInfoBuffer;
    private ComputeBuffer _projPosBuffer;
    private ComputeBuffer _pDensityBuffer;
    private ComputeBuffer _indexRangeBuffer;
    private ComputeBuffer _spatialBuffer;
    private ComputeBuffer _indexRangeSampleBuffer;
    private ComputeBuffer _spatialSampleBuffer;

    private Rectangle _bounds;
    
    
    private Particle[] _particles;
    private Particle[] _circleStates;
    private Rectangle[] _rectangles;
    private CircleInfo[] _circleInfo;
    // private NativeArray<CollisionInfo> _collisionInfo;
    private CollisionInfo[] _collisionInfo;
    private float[] _particleDensities;
    private float2[] _projPositions;

    private int2[] _spatialLookup;
    private int2[] _cIndexRanges;
    private int2[] _spatialLookupSample;
    private int2[] _cIndexRangesSample;
    private int2[] _ranges;
    
    private float _invDistributionVolume;
    private float _invInfluenceRadius;
    private float _invPullRadius;
    private float _invSampleRadius;
    private float _invSampleVolume;
    
    private float2 pullPosition = new(100, 100);
    private int2 _boundsSizeRadii;
    
    private int _textureKernel;
    private int _densityKernel;
    private int _projPosKernel;
    private int _updateKernel;

    private SpatialLookup _lookup;
    private SpatialLookup _sampleLookup;
    
    private AsyncGPUReadbackRequest _collisionReadback;

    public struct Rectangle
    {
        public float2 Position;
        public float2x2 _rs;
        public float2x2 _invrs;

        public Rectangle(float2 position, float2 size, float rotation)
        {
            Position = position;
            
            _rs = math.mul(float2x2.Rotate(rotation), float2x2.Scale(size));
            _invrs = math.inverse(_rs);
        }

        public void SetTRS(float2 pos, float2 size, float rotation)
        {
            Position = pos;
            
            _rs = math.mul(float2x2.Rotate(rotation), float2x2.Scale(size));
            _invrs = math.inverse(_rs);
        }

        public float2 PointToLocal(float2 worldPoint)
        {
            return math.mul(_invrs, worldPoint);
        }

        public float2 PointToWorld(float2 localPoint)
        {
            return math.mul(_rs, localPoint);
        }
    }

    public struct CircleInfo
    {
        public float Radius;
        public float Mass;
        public float Density;
        public CircleInfo(float radius, float mass)
        {
            Radius = radius; 
            Mass = mass;
            Density = mass / (Mathf.PI * radius * radius);
        }
    }

    private struct CollisionInfo
    {
        public float2 Impulse;
        public int CircleIndex;
    }
    
    public struct Particle
    {
        public float2 Position;
        public float2 Velocity;

        public Particle(Vector2 position, Vector2 velocity)
        {
            Position = position;
            Velocity = velocity;
        }
    }
    
    
    // Start is called before the first frame update
    void Start()
    {
        Graphics.ClearRandomWriteTargets();
        InitData();
        InitGraphics();
        InitCompute();
    }

    void OnDestroy()
    {
        if (!isActiveAndEnabled) return;
        
        Graphics.ClearRandomWriteTargets();
        
        _particleBuffer.Release();
        _projPosBuffer.Release();
        _rectangleBuffer.Release();
        _circleInfoBuffer.Release();
        _circleStateBuffer.Release();
        _pDensityBuffer.Release();
        _spatialBuffer.Release();
        _indexRangeBuffer.Release();
        _spatialSampleBuffer.Release();
        _indexRangeSampleBuffer.Release();
        _collisionInfoBuffer.Release();

        _lookup.Destroy();
        _sampleLookup.Destroy();
    }

    private void OnValidate()
    {
        _boundsSizeRadii = new int2((int)(boundsSize.x / influenceRadius) + 1, (int)(boundsSize.y / influenceRadius) + 1);
        _boundsSizeRadii = _boundsSizeRadii.x % 2 == 0 ? _boundsSizeRadii : _boundsSizeRadii + 1;

    }

    void InitData()
    {
        _bounds = new Rectangle(0, new float2(_boundsSizeRadii.x * influenceRadius, _boundsSizeRadii.y * influenceRadius) / 2f, boundsRotation);
        
        _invDistributionVolume = 1f / (influenceRadius * influenceRadius / 10);
        _invInfluenceRadius = 1f / influenceRadius;
        _invPullRadius = 1f / pullRadius;
        _invSampleRadius = 1f / sampleRadius;
        _invSampleVolume = 1f / (sampleRadius * sampleRadius / 10);

        _particleDensities = new float[numParticles];
        _particles = new Particle[numParticles];
        _projPositions = new float2[numParticles];
        _spatialLookup = new int2[numParticles];
        _spatialLookupSample = new int2[numParticles];
        _cIndexRanges = new int2[numParticles];
        _cIndexRangesSample = new int2[numParticles];
        //_collisionInfo = new NativeArray<CollisionInfo>(numParticles, Allocator.Persistent);
        _collisionInfo = new CollisionInfo[numParticles];
        _ranges = new int2[9];
        
        for (int i = 0; i < numParticles; i++)
        {
            float2 position = Random.insideUnitCircle;
            _particles[i] = new Particle(startRadius * position, Random.insideUnitCircle);
            _spatialLookup[i] = new(i, HashPosition(position, influenceRadius));
            _spatialLookupSample[i] = new(i, HashPosition(position, sampleRadius));
            _collisionInfo[i] = new();
        }

        _rectangles = new Rectangle[rectangles.Length + 1];
        _rectangles[0] = _bounds;
        for (int i = 0; i < rectangles.Length; i++)
        {
            _rectangles[i + 1] = new Rectangle((Vector2)rectangles[i].position, (Vector2)rectangles[i].localScale * .5f, Mathf.Deg2Rad * rectangles[i].eulerAngles.z);
        }
        
        _circleInfo = new CircleInfo[circles.Length];
        _circleStates = new Particle[circles.Length];
        for (int i = 0; i < circles.Length; i++)
        {
            _circleStates[i] = new Particle(circles[i].position, circles[i].linearVelocity);
            _circleInfo[i] = new CircleInfo(circles[i].transform.localScale.x * .5f, circles[i].mass);
        }
        
        _particleBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2 * 2);
        // Graphics.SetRandomWriteTarget(2, _particleBuffer, false);
        _particleBuffer.SetData(_particles);

        _rectangleBuffer = new ComputeBuffer(_rectangles.Length, sizeof(float) * 4 * 2 + sizeof(float) * 2);
        _rectangleBuffer.SetData(_rectangles);
        
        _circleInfoBuffer = new ComputeBuffer(_circleInfo.Length, sizeof(float) * 3);
        _circleInfoBuffer.SetData(_circleInfo);
        
        _circleStateBuffer = new ComputeBuffer(_circleStates.Length, sizeof(float) * 2 * 2);
        _circleStateBuffer.SetData(_circleStates);

        _collisionInfoBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2 + sizeof(int));
        _collisionInfoBuffer.SetData(_collisionInfo);
       
        
        _pDensityBuffer = new ComputeBuffer(numParticles, sizeof(float));
        _pDensityBuffer.SetData(_particleDensities);
        
       
        _projPosBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);
        _projPosBuffer.SetData(_projPositions);
        
        _indexRangeBuffer = new ComputeBuffer(numParticles, sizeof(int) * 2);
        _indexRangeBuffer.SetData(_cIndexRanges);
        
        _spatialBuffer = new ComputeBuffer(numParticles, sizeof(int) * 2);
        _spatialBuffer.SetData(_spatialLookup);
        
        _indexRangeSampleBuffer = new ComputeBuffer(numParticles, sizeof(int) * 2);
        _indexRangeSampleBuffer.SetData(_cIndexRangesSample);
        
        _spatialSampleBuffer = new ComputeBuffer(numParticles, sizeof(int) * 2);
        _spatialSampleBuffer.SetData(_spatialLookupSample);
        
        _lookup = new SpatialLookup(numParticles, influenceRadius, _projPosBuffer, _spatialBuffer, _indexRangeBuffer);
        _sampleLookup = new SpatialLookup(numParticles, sampleRadius, _projPosBuffer, _spatialSampleBuffer, _indexRangeSampleBuffer);
        
        _textureKernel = fluidCompute.FindKernel("CreateTexture");
        _densityKernel = fluidCompute.FindKernel("CalculateDensities");
        _projPosKernel = fluidCompute.FindKernel("ProjectPositions");
        _updateKernel = fluidCompute.FindKernel("UpdateParticles");
    }

    void InitGraphics()
    {
        _fluidSampleTexture = new RenderTexture(textureSize.x, textureSize.y, 24, DefaultFormat.HDR);
        _fluidSampleTexture.enableRandomWrite = true;
        _fluidSampleTexture.Create();
        
        fluidMaterial.SetInt(NumParticlesID, numParticles);
        fluidMaterial.SetBuffer(ParticlesID, _particleBuffer);
        fluidMaterial.SetTexture("_FluidSampleTex", _fluidSampleTexture);
    }

    void InitCompute()
    {
        fluidCompute.SetTexture(_textureKernel, ResultID, _fluidSampleTexture);
        
        fluidCompute.SetBuffer(_textureKernel, ParticlesID, _particleBuffer);
        fluidCompute.SetBuffer(_densityKernel, ParticlesID, _particleBuffer);
        fluidCompute.SetBuffer(_projPosKernel, ParticlesID, _particleBuffer);
        fluidCompute.SetBuffer(_updateKernel, ParticlesID, _particleBuffer);
        
        fluidCompute.SetBuffer(_densityKernel, "p_densities", _pDensityBuffer);
        fluidCompute.SetBuffer(_updateKernel, "p_densities", _pDensityBuffer);
        
        
        fluidCompute.SetBuffer(_densityKernel, "proj_positions", _projPosBuffer);
        fluidCompute.SetBuffer(_projPosKernel, "proj_positions", _projPosBuffer);
        fluidCompute.SetBuffer(_updateKernel, "proj_positions", _projPosBuffer);
        
        fluidCompute.SetBuffer(_updateKernel, "rectangles", _rectangleBuffer);
        
        fluidCompute.SetBuffer(_updateKernel, "circle_info", _circleInfoBuffer);
        // fluidCompute.SetBuffer(_textureKernel, "circle_info", _circleInfoBuffer);
        
        fluidCompute.SetBuffer(_updateKernel, "circle_state", _circleStateBuffer);
        // fluidCompute.SetBuffer(_textureKernel, "circle_state", _circleStateBuffer);
        
        fluidCompute.SetBuffer(_updateKernel, "collision_info", _collisionInfoBuffer);
        
        fluidCompute.SetBuffer(_textureKernel, "c_index_ranges_s", _indexRangeSampleBuffer);
        fluidCompute.SetBuffer(_textureKernel, "spatial_lookup_s", _spatialSampleBuffer); 
        fluidCompute.SetBuffer(_densityKernel, "c_index_ranges", _indexRangeBuffer);
        fluidCompute.SetBuffer(_densityKernel, "spatial_lookup", _spatialBuffer);
        fluidCompute.SetBuffer(_updateKernel, "c_index_ranges", _indexRangeBuffer);
        fluidCompute.SetBuffer(_updateKernel, "spatial_lookup", _spatialBuffer);
        
        fluidCompute.SetInt(NumParticlesID, numParticles);
        fluidCompute.SetInt("num_rectangles", rectangles.Length);
        fluidCompute.SetInt("num_circles", circles.Length);
        fluidCompute.SetFloat("inv_distribution_volume", _invDistributionVolume);
        fluidCompute.SetFloat("inv_distribution_volume_s", _invSampleVolume);
        fluidCompute.SetFloat("inv_influence_radius", _invInfluenceRadius);
        fluidCompute.SetFloat("inv_pull_radius", _invPullRadius);
        fluidCompute.SetFloat("inv_sample_radius", _invSampleRadius);
        fluidCompute.SetVector("sample_dimensions", sampleArea);
        fluidCompute.SetVector("sample_origin", -(sampleArea * 0.5f));
        fluidCompute.SetInts("texture_dimensions", new int[]{textureSize.x, textureSize.y});
        fluidCompute.SetFloat("target_density", targetDensity);
        fluidCompute.SetFloat("pressure_multiplier", pressureMultiplier);
        fluidCompute.SetFloat("influence_radius", influenceRadius);
        fluidCompute.SetFloat("sample_radius", sampleRadius);
        fluidCompute.SetFloats("gravity", new float[]{gravity.x, gravity.y});
        fluidCompute.SetFloat("sim_timestep", simTimeStep);
        fluidCompute.SetFloat("pull_strength", pullStrength);
        fluidCompute.SetFloat("particle_mass", totalMass / numParticles);
        fluidCompute.SetFloat("bounciness", bounciness);
        fluidCompute.SetFloat("circle_friction", circleFriction);
        fluidCompute.SetFloat("viscosity", viscosity);
        fluidCompute.SetFloat("circle_surface_strength", circleSurfaceStrength);
        fluidCompute.SetFloats("bounds", new float[]{_bounds.Position.x, _bounds.Position.y, 
                                                          _bounds._rs.c0.x, _bounds._rs.c0.y, _bounds._rs.c1.x, _bounds._rs.c1.y,
                                                          _bounds._invrs.c0.x, _bounds._invrs.c0.y, _bounds._invrs.c1.x, _bounds._invrs.c1.y });
        fluidCompute.SetInt("bound_half_width_radii", _boundsSizeRadii.x / 2);
    }
    
    static int UnsignedRightShift(int s, int i)
    {
        int y = (int)((uint)s >> i);
        return y;
    }
    
    int2 GetQuantizedCoord(float2 position, float cellSize)
    {
        return (int2)(math.floor(position / cellSize));
    }
    
    int HashPosition(int2 coord)
    {
        int x = (coord.x * (coord.x < 0 ? 50331653 : 12582917));
        int y = (coord.y * (coord.y < 0 ? 786433 : 196613));
        
        return UnsignedRightShift(((3145739 + x) * 25165843 + y), 1) % numParticles;
    }
    
    int HashPosition(float2 position, float cellSize)
    {
        return HashPosition(GetQuantizedCoord(position, cellSize));
    }
    
    void SimulationStep()
    {
        fluidCompute.Dispatch(_projPosKernel, numParticles / 64 + 1, 1, 1);

        UpdateCircles();
        
        fluidCompute.SetFloat("delta_time", Mathf.Min(minDt, Time.deltaTime));
        _circleStateBuffer.SetData(_circleStates);
        
        _sampleLookup.UpdateSpatialLookupGPU();
        
        _lookup.UpdateSpatialLookupGPU();
        
        
        fluidCompute.Dispatch(_densityKernel, numParticles / 64 + 1, 1, 1);

        fluidCompute.Dispatch(_updateKernel, numParticles / 64 + 1, 1, 1);
    }
    

    void UpdateCircles()
    {
        for (int i = 0; i < circles.Length; i++)
        {
            _circleStates[i].Position = circles[i].position;
            _circleStates[i].Velocity = circles[i].linearVelocity;
        }
    }

    void ApplyCircleForces()
    {
        float2[] impulses = new float2[circles.Length];
        for (int i = 0; i < _collisionInfo.Length; i++)
        {
            if (_collisionInfo[i].CircleIndex != -1)
            {
                impulses[_collisionInfo[i].CircleIndex] += _collisionInfo[i].Impulse;
            }
        }
        for (int i = 0; i < circles.Length; i++)
        {
            circles[i].AddForce(impulses[i] / Time.fixedDeltaTime);
        }
    }

    void UpdateRectangles()
    {
        for (int i = 0; i < rectangles.Length; i++)
        {
            _rectangles[i].SetTRS((Vector2)rectangles[i].position, (Vector2)rectangles[i].localScale * .5f, Mathf.Deg2Rad * rectangles[i].eulerAngles.z);
        }
    }

    void FixedUpdate()
    {
        // if (_collisionReadback.done)
        // {
        //     ApplyCircleForces();
        //     // print("whatup");
        //     // UpdateCircles();
        //     // _circleStateBuffer.SetData(_circleStates);
        //     _collisionReadback = AsyncGPUReadback.RequestIntoNativeArray(ref _collisionInfo, _collisionInfoBuffer);
        // }
        
        _collisionInfoBuffer.GetData(_collisionInfo);
        ApplyCircleForces();
        
    }

    void DrawGrid()
    {
        float2 _boundsSize = (float2)_boundsSizeRadii * influenceRadius;
        Vector3 lineStart = Vector3.down * _boundsSize.y * .5f + Vector3.left * _boundsSize.x * .5f;
        float lineLength = _boundsSize.y;
        Vector3 offset = Vector3.right * influenceRadius;
        for (int i = 0; i < _boundsSizeRadii.x; i++)
        {
            Debug.DrawRay(lineStart + i * offset, Vector3.up * lineLength, Color.red);
        }
        
        lineLength = _boundsSize.x;
        offset = Vector3.up * influenceRadius;
        for (int i = 0; i < _boundsSizeRadii.y; i++)
        {
            Debug.DrawRay(lineStart + i * offset, Vector3.right * lineLength, Color.red);
        }
    }

    // Update is called once per frame
    void Update()
    {
        //DrawGrid();
        // UpdateRectangles();
        // _rectangleBuffer.SetData(_rectangles);
        Debug.DrawLine(Vector3.zero, Vector3.up, Color.red);
        SimulationStep();
        
        if (Input.GetMouseButton(0))
        {
            Vector3 pos = Camera.main.ScreenToWorldPoint(Input.mousePosition) - Vector3.forward;
            //print(GetQuantizedCoord(new float2(pos.x, pos.y), influenceRadius));
            pullPosition = (Vector2)pos;
        }
        else
        {
            pullPosition = new(100, 100);
        }
        
        fluidCompute.SetVector(PullPositionID, (Vector2)pullPosition);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        fluidCompute.Dispatch(_textureKernel, textureSize.x / 8, textureSize.y / 8, 1);
        
        Graphics.Blit(source, destination, fluidMaterial);
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireCube(Vector3.zero, (Vector2)new float2(_boundsSizeRadii.x * influenceRadius, _boundsSizeRadii.y * influenceRadius));
    }
}
