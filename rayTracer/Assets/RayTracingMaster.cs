using System.Collections;
using System.Collections.Generic;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UI;

public class RayTracingMaster : MonoBehaviour
{
    [Header("Ray Tracing")]
    public ComputeShader RayTracingShader;
    public Texture SkyboxTexture;
    public Light DirectionalLight;

    [Header("Debug")] 
    public Text SeedCountText;
    public Text SampleCountText;
    
    [Header("Samples")]
    public int maxSampleCount = 8192;
    
    [Header("Spheres")]
    public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
    public uint SpheresMax = 100;
    public float SpherePlacementRadius = 100.0f;
    public int SphereSeed;
    
    // Sphere Buffer
    private ComputeBuffer _sphereBuffer;
        
    // Camera
    private Camera _camera;
    private float _lastFieldOfView;
    
    // Render Texture
    private RenderTexture _target;
    private RenderTexture _converged;
    
    // Material
    private Material _addMaterial;
    
    // Samples
    private uint _currentSample = 0;
    
    // Other
    private List<Transform> _transformsToWatch = new List<Transform>();
    private int _lastSeed;
    
    struct Sphere
    {
        public Vector3 position;
        public float radius;
        public Vector3 albedo;
        public Vector3 specular;
        public float smoothness;
        public Vector3 emission;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        
        _transformsToWatch.Add(transform);
        _transformsToWatch.Add(DirectionalLight.transform);
    }
    
    private void Update()
    {
        // Keybindings
        
        // Screenshot
        if (Input.GetKeyDown(KeyCode.F12))
        {
            // Disable
            SeedCountText.enabled = false;
            SampleCountText.enabled = false;
            
            // Screenshot 0-0
            ScreenCapture.CaptureScreenshot( "Images/Seed_" + SphereSeed + " - sampleCount_" + _currentSample + ".png");
            
            // Re-enables text
            StartCoroutine(RestoreText());
        }
        
        // Change Seed
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKey(KeyCode.D))
        {
            SphereSeed++;
        }else if (Input.GetKeyDown(KeyCode.D) && !Input.GetKey(KeyCode.LeftShift))
        {
            SphereSeed++;
        }else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKey(KeyCode.A))
        {
            SphereSeed--;
        }else if (Input.GetKeyDown(KeyCode.A) && !Input.GetKey(KeyCode.LeftShift))
        {
            SphereSeed--;
        }

        // Change fov
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKey(KeyCode.W))
        {
            _camera.fieldOfView--;
        }else if (Input.GetKeyDown(KeyCode.W) && !Input.GetKey(KeyCode.LeftShift))
        {
            _camera.fieldOfView--;
        }else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKey(KeyCode.S))
        {
            _camera.fieldOfView++;
        }else if (Input.GetKeyDown(KeyCode.S) && !Input.GetKey(KeyCode.LeftShift))
        {
            _camera.fieldOfView++;
        }

        // Updates fov
        if (_camera.fieldOfView != _lastFieldOfView)
        {
            _currentSample = 0;
            _lastFieldOfView = _camera.fieldOfView;
        }
        
        // Updates transform
        foreach (Transform t in _transformsToWatch)
        {
            if (t.hasChanged)
            {
                _currentSample = 0;
                t.hasChanged = false;
            }
        }
        
        // Updates seed
        if (_lastSeed != SphereSeed)
        {
            _lastSeed = SphereSeed;     // Update lastSeed
            _currentSample = 0;         // Reset Samples
            SetUpScene();               // Regenerate spheres with new seed
        }
        
        // Updates text
        if (SeedCountText != null)
        {
            SeedCountText.text = "Seed = " + SphereSeed.ToString();
        }
    }

    private IEnumerator RestoreText()
    {
        yield return new WaitForSeconds(0.05f); // Wait
        SeedCountText.enabled = true;           // Re-enables text
        SampleCountText.enabled = true;
    }
    
    private void OnEnable()
    {
        _currentSample = 0; // Resets samples
        SetUpScene();       // Sets up scene
    }
    
    private void OnDisable()
    {
        if (_sphereBuffer != null)
            _sphereBuffer.Release();
    }

    private void SetUpScene()
    {
        Random.InitState(SphereSeed);
        List<Sphere> spheres = new List<Sphere>();

        // Add number of random spheres
        for (int i = 0; i < SpheresMax; i++)
        {
            Sphere sphere = new Sphere();
            sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);  // Randomly choose radius
            
            // Place spheres randomly within defined radius
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

            // Block spheres that collide
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    goto SkipSphere;
            }

            // Random material
            Color color = Random.ColorHSV();
            float chance = Random.value;
            if (chance < 0.9f)
            {
                // Albedo, Specular and Roughness
                bool metal = chance < 0.4f;
                sphere.albedo = metal ? Vector4.zero : new Vector4(color.r, color.g, color.b);
                sphere.specular = metal ? new Vector4(color.r, color.g, color.b) : new Vector4(0.04f, 0.04f, 0.04f);
                sphere.smoothness = Random.value;
            }
            else
            {
                // Emission
                Color emission = Random.ColorHSV(0, 1, 0, 1, 3.0f, 8.0f);
                sphere.emission = new Vector3(emission.r, emission.g, emission.b);
            }
            // Add sphere to list
            spheres.Add(sphere);

            SkipSphere:
            continue;
        }

        // Assign to compute buffer
        if (_sphereBuffer != null)
            _sphereBuffer.Release();
        if (spheres.Count > 0)
        {
            _sphereBuffer = new ComputeBuffer(spheres.Count, 56);
            _sphereBuffer.SetData(spheres);
        }
    }

    private void SetShaderParameters()
    {
        RayTracingShader.SetTexture(0, "_SkyboxTexture", SkyboxTexture);
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
        RayTracingShader.SetFloat("_Seed", Random.value);

        //Vector3 l = DirectionalLight.transform.forward;
        //RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));

        // Set buffer
        if (_sphereBuffer != null)
            RayTracingShader.SetBuffer(0, "_Spheres", _sphereBuffer);
    }
    
    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            // Release render texture if already have one
            if (_target != null)
            {
                _target.Release();
                _converged.Release();
            }

            // Get render target for Ray Tracing
            _target = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();
            _converged = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _converged.enableRandomWrite = true;
            _converged.Create();

            // Reset sampling
            _currentSample = 0;
        }
    }
    
    private void Render(RenderTexture destination)
    {
        // Make sure we have current render target
        InitRenderTexture();

        // Set target and dispatch compute shader
        RayTracingShader.SetTexture(0, "Result", _target);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // Blit result texture to screen
        if (_addMaterial == null)
            _addMaterial = new Material(Shader.Find("Hidden/AddShader"));
        _addMaterial.SetFloat("_Sample", _currentSample);
        Graphics.Blit(_target, _converged, _addMaterial);
        Graphics.Blit(_converged, destination);
        
        // Update text
        _currentSample++;
        if (SampleCountText != null) 
        { 
            SampleCountText.text = "Samples = " + _currentSample.ToString();
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_currentSample < maxSampleCount)
        {
            SetShaderParameters();
            Render(destination);
        }
        
        Graphics.Blit(_converged, destination);
    }
}
