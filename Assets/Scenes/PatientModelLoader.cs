using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public class PatientModelLoader : MonoBehaviour
{
    [Header("Server")]
    public string baseUrl = "http://172.20.10.2:55555";
    public float pollingInterval = 3f;
    [Tooltip("Unity Play Mode에서는 같은 PC의 서버를 localhost로 호출합니다.")]
    public bool useLocalhostInEditor = true;

    [Header("PatientAnchor children")]
    public MeshFilter skinMeshFilter;
    public MeshFilter tumorMeshFilter;
    public GameObject tumorGameObject;
    public Transform previewAnchor;

    private string currentPatient;
    private string currentSkinUrl;
    private string currentTumorUrl;
    private string currentModelRevision;
    private Mesh runtimeSkinMesh;
    private Mesh runtimeTumorMesh;
    private Transform skinModelRoot;
    private Transform tumorModelRoot;
    private Vector3 skinRootPosition;
    private Vector3 tumorRootPosition;
    private Quaternion skinRootRotation;
    private Quaternion tumorRootRotation;
    private bool hasDisplayReferenceCenter;
    private Vector3 displayReferenceCenterInAnchor;
    private RegistrationManager registrationManager;

    public string CurrentPatientId { get { return currentPatient; } }
    public string LastError { get; private set; }

    [Serializable]
    private class PatientInfo
    {
        public string patient_id;
        public string skin_url;
        public string tumor_url;
        public string model_revision;
        public float preview_rotation_z_degrees;
    }

    [Serializable]
    private class CurrentPatientResponse
    {
        public string active_patient;
        public PatientInfo patient;
    }

    private void Start()
    {
        if (skinMeshFilter == null || tumorMeshFilter == null || tumorGameObject == null)
        {
            SetError("MeshFilter 또는 tumorGameObject 연결이 없습니다.");
            enabled = false;
            return;
        }

        registrationManager = FindObjectOfType<RegistrationManager>();
        if (previewAnchor == null)
        {
            if (registrationManager != null) previewAnchor = registrationManager.targetMeshAnchor;
        }
        // 환자를 바꿀 때 PatientAnchor 자체는 움직이지 않습니다. 환자별 초기
        // 방향 차이는 skin/tumor 모델 루트에만 적용하여 UI 슬라이더의 위치와
        // 이미 정합된 기준 위치를 보존합니다.
        skinModelRoot = skinMeshFilter.transform.parent != null
            ? skinMeshFilter.transform.parent : skinMeshFilter.transform;
        tumorModelRoot = tumorGameObject.transform;
        skinRootPosition = skinModelRoot.localPosition;
        tumorRootPosition = tumorModelRoot.localPosition;
        skinRootRotation = skinModelRoot.localRotation;
        tumorRootRotation = tumorModelRoot.localRotation;

        Debug.Log("PatientModelLoader 서버 주소: " + GetServerUrl("/"));

        // Inspector의 초기 Mesh가 None이어도 앱 시작 즉시 서버에서 현재 환자를 받습니다.
        ClearModels();
        StartCoroutine(PollPatientStatus());
    }

    private IEnumerator PollPatientStatus()
    {
        while (true)
        {
            string currentPatientUrl = GetServerUrl("/api/patient/current");
            using (UnityWebRequest request = UnityWebRequest.Get(currentPatientUrl))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (RequestFailed(request))
                {
                    SetError("환자 조회 실패 (" + currentPatientUrl + "): " + request.error);
                }
                else
                {
                    CurrentPatientResponse response = ParseCurrent(request.downloadHandler.text);
                    if (response != null && response.patient != null &&
                        !string.IsNullOrEmpty(response.active_patient) &&
                        response.active_patient == response.patient.patient_id &&
                        ModelSelectionChanged(response.patient))
                    {
                        Debug.Log("환자 선택 감지: " + response.active_patient);
                        Vector3 measuredDisplayCenter;
                        if (TryGetSkinCenterInAnchor(out measuredDisplayCenter))
                        {
                            displayReferenceCenterInAnchor = measuredDisplayCenter;
                            hasDisplayReferenceCenter = true;
                        }
                        // 다른 환자의 모델이 보이는 동안 정합되는 일을 막습니다.
                        ClearModels();
                        ApplyPreviewModelOrientation(response.patient);
                        yield return DownloadAndApplyModels(
                            response.patient,
                            hasDisplayReferenceCenter,
                            displayReferenceCenterInAnchor);
                    }
                }
            }

            yield return new WaitForSeconds(Mathf.Max(1f, pollingInterval));
        }
    }

    private IEnumerator DownloadAndApplyModels(
        PatientInfo patient,
        bool preserveDisplayCenter,
        Vector3 previousDisplayCenter)
    {
        if (string.IsNullOrEmpty(patient.skin_url))
        {
            SetError(patient.patient_id + "의 skin_url이 없습니다.");
            yield break;
        }

        Mesh newSkin = null;
        Mesh newTumor = null;

        using (UnityWebRequest skinRequest = UnityWebRequest.Get(GetServerUrl(patient.skin_url)))
        {
            skinRequest.timeout = 30;
            yield return skinRequest.SendWebRequest();
            if (RequestFailed(skinRequest))
            {
                SetError("피부 OBJ 다운로드 실패: HTTP " + skinRequest.responseCode + " " + skinRequest.error);
                yield break;
            }

            Debug.Log("피부 OBJ 수신: " + patient.skin_url + " (" + skinRequest.downloadHandler.data.Length + " bytes)");
            newSkin = ParseObjMesh(skinRequest.downloadHandler.data, "skin");
        }

        if (newSkin == null) yield break;

        if (!string.IsNullOrEmpty(patient.tumor_url))
        {
            using (UnityWebRequest tumorRequest = UnityWebRequest.Get(GetServerUrl(patient.tumor_url)))
            {
                tumorRequest.timeout = 30;
                yield return tumorRequest.SendWebRequest();
                if (tumorRequest.responseCode == 404)
                {
                    Debug.LogWarning("병변 OBJ가 없어 피부만 표시합니다: " + patient.tumor_url);
                }
                else if (RequestFailed(tumorRequest))
                {
                    Debug.LogWarning("병변 OBJ를 받을 수 없어 피부만 표시합니다: " + tumorRequest.error);
                }
                else
                {
                    Debug.Log("병변 OBJ 수신: " + patient.tumor_url + " (" + tumorRequest.downloadHandler.data.Length + " bytes)");
                    newTumor = ParseObjMesh(tumorRequest.downloadHandler.data, "tumor");
                    if (newTumor == null)
                        Debug.LogWarning("병변 OBJ 해석 실패. 피부만 표시합니다.");
                }
            }
        }

        // 다운로드 도중 웹 UI에서 환자가 바뀌었으면 오래된 모델을 적용하지 않습니다.
        using (UnityWebRequest checkRequest = UnityWebRequest.Get(GetServerUrl("/api/patient/current")))
        {
            checkRequest.timeout = 15;
            yield return checkRequest.SendWebRequest();
            CurrentPatientResponse latest = RequestFailed(checkRequest)
                ? null : ParseCurrent(checkRequest.downloadHandler.text);
            if (latest == null || latest.patient == null ||
                latest.active_patient != patient.patient_id ||
                !SameModelDescriptor(latest.patient, patient))
            {
                Debug.LogWarning("모델 다운로드 중 환자가 바뀌었습니다. 다시 확인합니다.");
                Destroy(newSkin);
                if (newTumor != null) Destroy(newTumor);
                yield break;
            }
        }

        skinMeshFilter.sharedMesh = newSkin;
        tumorMeshFilter.sharedMesh = newTumor;
        runtimeSkinMesh = newSkin;
        runtimeTumorMesh = newTumor;

        // 환자 OBJ마다 원점과 좌표 범위가 크게 다릅니다. 이전 피부 모델의
        // 표시 중심에 새 피부 모델의 중심을 맞춰, 환자 교체 시 볼륨이
        // 슬라이더에서 멀어지거나 위아래로 튀는 현상을 막습니다.
        if (preserveDisplayCenter)
            AlignModelCenterTo(previousDisplayCenter);
        else
        {
            Vector3 firstDisplayCenter;
            if (TryGetSkinCenterInAnchor(out firstDisplayCenter))
            {
                displayReferenceCenterInAnchor = firstDisplayCenter;
                hasDisplayReferenceCenter = true;
            }
        }

        PositionUiBesideSkin();

        tumorGameObject.SetActive(newTumor != null);
        currentPatient = patient.patient_id;
        currentSkinUrl = patient.skin_url;
        currentTumorUrl = patient.tumor_url;
        currentModelRevision = patient.model_revision;
        LastError = null;
        Debug.Log("환자 모델 적용 완료: " + currentPatient +
            " / skin vertices=" + newSkin.vertexCount +
            " / tumor=" + (newTumor != null ? "yes" : "no"));
    }

    private void ClearModels()
    {
        if (skinMeshFilter != null) skinMeshFilter.sharedMesh = null;
        if (tumorMeshFilter != null) tumorMeshFilter.sharedMesh = null;
        if (tumorGameObject != null) tumorGameObject.SetActive(false);
        if (runtimeSkinMesh != null) Destroy(runtimeSkinMesh);
        if (runtimeTumorMesh != null) Destroy(runtimeTumorMesh);
        runtimeSkinMesh = null;
        runtimeTumorMesh = null;
        currentPatient = null;
        currentSkinUrl = null;
        currentTumorUrl = null;
        currentModelRevision = null;
        LastError = null;
    }

    private bool ModelSelectionChanged(PatientInfo patient)
    {
        return patient.patient_id != currentPatient ||
            patient.skin_url != currentSkinUrl ||
            patient.tumor_url != currentTumorUrl ||
            patient.model_revision != currentModelRevision;
    }

    private static bool SameModelDescriptor(PatientInfo left, PatientInfo right)
    {
        return left != null && right != null &&
            left.patient_id == right.patient_id &&
            left.skin_url == right.skin_url &&
            left.tumor_url == right.tumor_url &&
            left.model_revision == right.model_revision;
    }

    private void ApplyPreviewModelOrientation(PatientInfo patient)
    {
        float correctionDegrees = patient != null
            ? patient.preview_rotation_z_degrees : 0f;
        if (float.IsNaN(correctionDegrees) || float.IsInfinity(correctionDegrees))
            correctionDegrees = 0f;
        Quaternion correction = Mathf.Abs(correctionDegrees) > 0.001f
            ? Quaternion.AngleAxis(correctionDegrees, Vector3.forward)
            : Quaternion.identity;

        SetModelRootPose(skinModelRoot, skinRootPosition, skinRootRotation, correction);
        SetModelRootPose(tumorModelRoot, tumorRootPosition, tumorRootRotation, correction);

        if (Mathf.Abs(correctionDegrees) > 0.001f)
            Debug.Log("환자 미리보기 방향 보정: " + patient.patient_id +
                " / Z=" + correctionDegrees.ToString("F1") + " degrees");
    }

    private bool TryGetSkinCenterInAnchor(out Vector3 centerInAnchor)
    {
        centerInAnchor = Vector3.zero;
        if (previewAnchor == null || skinMeshFilter == null || skinMeshFilter.sharedMesh == null)
            return false;

        Vector3 centerWorld = skinMeshFilter.transform.TransformPoint(
            skinMeshFilter.sharedMesh.bounds.center);
        centerInAnchor = previewAnchor.InverseTransformPoint(centerWorld);
        return true;
    }

    private void AlignModelCenterTo(Vector3 desiredCenterInAnchor)
    {
        Vector3 currentCenterInAnchor;
        if (!TryGetSkinCenterInAnchor(out currentCenterInAnchor) || previewAnchor == null)
            return;

        Vector3 currentCenterWorld = previewAnchor.TransformPoint(currentCenterInAnchor);
        Vector3 desiredCenterWorld = previewAnchor.TransformPoint(desiredCenterInAnchor);
        Vector3 worldDelta = desiredCenterWorld - currentCenterWorld;

        if (skinModelRoot != null) skinModelRoot.position += worldDelta;
        if (tumorModelRoot != null) tumorModelRoot.position += worldDelta;

        Debug.Log("환자 교체 표시 중심 보정: " +
            (worldDelta * 1000f).ToString("F1") + " mm");
    }

    private void PositionUiBesideSkin()
    {
        Vector3 centerInAnchor;
        if (registrationManager != null && TryGetSkinCenterInAnchor(out centerInAnchor))
            registrationManager.PlaceUiPanelBesideSkin(centerInAnchor);
    }

    // 서버 정합 행렬은 원본 OBJ 좌표계를 기준으로 계산되므로, 정합 결과를
    // 적용하기 직전에 환자별 미리보기 회전과 중심 이동을 제거합니다.
    public void PrepareModelForRegistration()
    {
        SetModelRootPose(skinModelRoot, skinRootPosition, skinRootRotation, Quaternion.identity);
        SetModelRootPose(tumorModelRoot, tumorRootPosition, tumorRootRotation, Quaternion.identity);
        PositionUiBesideSkin();
    }

    private static void SetModelRootPose(
        Transform modelRoot,
        Vector3 basePosition,
        Quaternion baseRotation,
        Quaternion correction)
    {
        if (modelRoot == null) return;
        modelRoot.localPosition = correction * basePosition;
        modelRoot.localRotation = correction * baseRotation;
    }

    private Mesh ParseObjMesh(byte[] bytes, string name)
    {
        Mesh mesh = null;
        try
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 20 * 1024 * 1024)
                throw new InvalidDataException("OBJ 크기가 비어 있거나 20 MB를 넘습니다.");

            // 이 프로젝트의 OBJ는 v/f 좌표만 필요합니다. MTL과 Dummiesman 임시
            // GameObject 생성 없이 바로 Inspector에 연결된 MeshFilter에 넣습니다.
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("v ") || line.StartsWith("v\t"))
                    {
                        string[] parts = SplitFields(line);
                        if (parts.Length < 4) throw new InvalidDataException("잘못된 v 행");
                        float x = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        float y = float.Parse(parts[2], CultureInfo.InvariantCulture);
                        float z = float.Parse(parts[3], CultureInfo.InvariantCulture);
                        // Dummiesman 부모의 X=-1 효과를 정점에 반영합니다.
                        vertices.Add(new Vector3(-x, y, z));
                    }
                    else if (line.StartsWith("f ") || line.StartsWith("f\t"))
                    {
                        string[] parts = SplitFields(line);
                        if (parts.Length < 4) throw new InvalidDataException("잘못된 f 행");
                        int first = ReadVertexIndex(parts[1], vertices.Count);
                        int previous = ReadVertexIndex(parts[2], vertices.Count);
                        for (int i = 3; i < parts.Length; i++)
                        {
                            int next = ReadVertexIndex(parts[i], vertices.Count);
                            // X 반전으로 뒤집힌 앞면 방향을 복구합니다.
                            triangles.Add(first);
                            triangles.Add(next);
                            triangles.Add(previous);
                            previous = next;
                        }
                    }
                }
            }

            if (vertices.Count == 0 || triangles.Count == 0)
                throw new InvalidDataException("OBJ에 정점 또는 삼각형이 없습니다.");
            if (vertices.Count > 65535 && !SystemInfo.supports32bitsIndexBuffer)
                throw new InvalidDataException("이 기기는 32비트 메쉬 인덱스를 지원하지 않습니다. OBJ 정점을 65535개 이하로 줄여주세요.");

            mesh = new Mesh();
            mesh.name = "Downloaded " + name;
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Debug.Log("OBJ 해석 완료: " + name + " / vertices=" + vertices.Count +
                " / triangles=" + triangles.Count / 3);
            return mesh;
        }
        catch (Exception error)
        {
            if (mesh != null) Destroy(mesh);
            SetError(name + " OBJ 해석 실패: " + error.Message);
            return null;
        }
    }

    private static string[] SplitFields(string line)
    {
        return line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int ReadVertexIndex(string field, int vertexCount)
    {
        int slash = field.IndexOf('/');
        string value = slash < 0 ? field : field.Substring(0, slash);
        int raw = int.Parse(value, CultureInfo.InvariantCulture);
        int index = raw > 0 ? raw - 1 : vertexCount + raw;
        if (index < 0 || index >= vertexCount)
            throw new InvalidDataException("OBJ 면의 정점 번호가 범위를 벗어났습니다: " + field);
        return index;
    }

    private static CurrentPatientResponse ParseCurrent(string json)
    {
        try { return JsonConvert.DeserializeObject<CurrentPatientResponse>(json); }
        catch (Exception error)
        {
            Debug.LogError("환자 JSON 파싱 실패: " + error.Message);
            return null;
        }
    }

    public string GetServerUrl(string path)
    {
#if UNITY_EDITOR
        string selectedBaseUrl = useLocalhostInEditor
            ? "http://127.0.0.1:55555"
            : baseUrl;
#else
        string selectedBaseUrl = baseUrl;
#endif
        return selectedBaseUrl.TrimEnd('/') + (path.StartsWith("/") ? path : "/" + path);
    }

    private static bool RequestFailed(UnityWebRequest request)
    {
        return request.isNetworkError || request.isHttpError;
    }

    private void SetError(string message)
    {
        LastError = message;
        Debug.LogError("PatientModelLoader: " + message);
    }

    private void OnDestroy()
    {
        ClearModels();
    }
}
