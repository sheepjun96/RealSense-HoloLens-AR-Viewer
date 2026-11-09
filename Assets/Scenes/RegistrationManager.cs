using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using TMPro;

public class RegistrationManager : MonoBehaviour
{
    private const string RegistrationPath = "/hololens/capture-register";

    public Transform targetMeshAnchor;
    public GameObject loadingUI;
    public TextMeshPro loadingText;
    public PatientModelLoader patientModelLoader;

    [Header("Patient-side controls")]
    public Transform uiPanel;
    [Tooltip("PatientAnchor 로컬 X축 기준으로 피부 모델 오른쪽에 둘 슬라이더 패널 위치 (미터)")]
    public float uiPanelLocalX = 0.2f;
    [Tooltip("피부 중심에서 UI_Panel 원점까지 오른쪽 거리 (미터)")]
    public float uiPanelRightOffsetFromSkinCenterMeters = 0.1f;

    [Header("Loading UI fixed at the capture camera pose")]
    public float loadingUIDistanceMeters = 0.8f;
    public float loadingUIVerticalOffsetMeters = 0.05f;

    private Vector3 captureCameraPosition;
    private Quaternion captureCameraRotation;

    [Header("Manual offset: right, up, forward (meters)")]
    public Vector3 manualOffsetMeters = new Vector3(0.00f, 0.22f, 0.03f);
    public bool applyManualOffset = true;

    [Header("Temporary XYZ calibration")]
    public bool enableTemporaryOffsetCalibration = true;

    private bool isRegistering;
    private TemporaryOffsetCalibration temporaryCalibration;
    private Vector3 registeredBaseWorldPosition;
    private Quaternion registeredOffsetFrameRotation;
    private Vector3 temporaryOffsetMeters;
    private bool hasRegisteredPose;
    private string registeredPatientId;

    private void Awake()
    {
        // 확정 수동 오프셋과 런타임 데모 UI 코드는 한 번의 빌드에 항상 포함합니다.
        // 실제 XYZ UI 노출 여부는 서버의 /api/runtime-config가 결정합니다.
        applyManualOffset = true;
        enableTemporaryOffsetCalibration = true;

        if (patientModelLoader == null)
            patientModelLoader = FindObjectOfType<PatientModelLoader>();
        if (uiPanel == null && targetMeshAnchor != null)
            uiPanel = targetMeshAnchor.Find("UI_Panel");
        if (uiPanel == null)
        {
            GameObject found = GameObject.Find("UI_Panel");
            if (found != null) uiPanel = found.transform;
        }
        if (uiPanel == null)
            Debug.LogError("RegistrationManager: UI_Panel을 찾지 못했습니다.");
        else if (targetMeshAnchor != null && uiPanel.parent == targetMeshAnchor)
        {
            Vector3 panelPosition = uiPanel.localPosition;
            panelPosition.x = uiPanelLocalX;
            uiPanel.localPosition = panelPosition;
        }

        // 투명도 슬라이더는 PatientAnchor에 그대로 둡니다. Loading_UI만
        // 분리해 정합 시작 순간의 카메라 앞 월드 위치에 고정합니다.
        if (loadingUI != null)
        {
            loadingUI.transform.SetParent(null, true);
            loadingUI.SetActive(false);
        }

        if (enableTemporaryOffsetCalibration && uiPanel != null)
        {
            temporaryCalibration = GetComponent<TemporaryOffsetCalibration>();
            if (temporaryCalibration == null)
                temporaryCalibration = gameObject.AddComponent<TemporaryOffsetCalibration>();
            temporaryCalibration.Initialize(this, uiPanel, loadingUI);
        }
    }

    private void LateUpdate()
    {
        if (hasRegisteredPose && patientModelLoader != null &&
            patientModelLoader.CurrentPatientId != registeredPatientId)
        {
            hasRegisteredPose = false;
            registeredPatientId = null;
            if (temporaryCalibration != null) temporaryCalibration.ResetForPatientChange();
        }
    }

    public void PlaceUiPanelBesideSkin(Vector3 skinCenterInAnchor)
    {
        if (uiPanel == null || targetMeshAnchor == null || uiPanel.parent != targetMeshAnchor)
            return;

        Vector3 panelPosition = uiPanel.localPosition;
        panelPosition.x = skinCenterInAnchor.x + uiPanelRightOffsetFromSkinCenterMeters;
        uiPanel.localPosition = panelPosition;
    }

    public void StartRegistrationProcess()
    {
        if (isRegistering) return;
        if (targetMeshAnchor == null || Camera.main == null)
        {
            Debug.LogError("RegistrationManager: targetMeshAnchor 또는 Main Camera가 없습니다.");
            ShowErrorFeedback("Setup Error");
            return;
        }
        if (patientModelLoader == null)
        {
            Debug.LogError("RegistrationManager: PatientModelLoader 연결이 없습니다.");
            ShowErrorFeedback("Model Setup Error");
            return;
        }
        if (string.IsNullOrEmpty(patientModelLoader.CurrentPatientId))
        {
            ShowErrorFeedback(string.IsNullOrEmpty(patientModelLoader.LastError)
                ? "Loading Model" : "Model Error");
            return;
        }

        captureCameraPosition = Camera.main.transform.position;
        captureCameraRotation = Camera.main.transform.rotation;
        PlaceLoadingUIAtPose(captureCameraPosition, captureCameraRotation);

        isRegistering = true;
        hasRegisteredPose = false;
        if (temporaryCalibration != null) temporaryCalibration.BeginRegistration();
        Debug.Log("정합 프로세스 시작...");
        if (loadingUI != null)
        {
            loadingUI.SetActive(true);
            if (loadingText != null) loadingText.text = "Loading...";
        }

        StartCoroutine(SendRegistrationRequest());
    }

    private IEnumerator SendRegistrationRequest()
    {
        string jsonData = "{}";
        string registrationUrl = patientModelLoader != null
            ? patientModelLoader.GetServerUrl(RegistrationPath)
            : "http://127.0.0.1:55555" + RegistrationPath;

        using (UnityWebRequest request = new UnityWebRequest(registrationUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.isNetworkError)
            {
                Debug.LogError("네트워크 에러 (" + registrationUrl + "): " + request.error);
                ShowErrorFeedback("Network Error");
            }
            else if (request.isHttpError)
            {
                Debug.LogError("API 에러 (" + registrationUrl + "): " + request.error);
                ShowErrorFeedback("Try Again");
            }
            else
            {
                string responseText = request.downloadHandler.text;
                RegistrationResponse response = null;
                try { response = JsonConvert.DeserializeObject<RegistrationResponse>(responseText); }
                catch (System.Exception error) { Debug.LogError("API JSON 해석 실패: " + error); }

                if (response != null && (response.status == "completed" || response.status == "available"))
                {
                    if (patientModelLoader != null && response.patient_id != patientModelLoader.CurrentPatientId)
                    {
                        Debug.LogError("정합 환자와 표시 모델이 다릅니다: " + response.patient_id + " / " + patientModelLoader.CurrentPatientId);
                        ShowErrorFeedback("Patient Changed");
                    }
                    else if (response.unit != "meter" && response.unit != "millimeter")
                    {
                        Debug.LogError("알 수 없는 행렬 단위: " + response.unit);
                        ShowErrorFeedback("Unit Error");
                    }
                    else if (IsValidMatrix(response.source_from_target_m))
                    {
                        Debug.Log("source_from_target_m translation (" + response.unit + "): " +
                            response.source_from_target_m[0][3] + ", " +
                            response.source_from_target_m[1][3] + ", " +
                            response.source_from_target_m[2][3]);
                        if (IsValidMatrix(response.source_from_target_mm))
                            Debug.Log("source_from_target_mm translation: " +
                                response.source_from_target_mm[0][3] + ", " +
                                response.source_from_target_mm[1][3] + ", " +
                                response.source_from_target_mm[2][3]);
                        patientModelLoader.PrepareModelForRegistration();
                        ApplyMatrix(response.source_from_target_m, response.unit == "meter");
                        if (loadingUI != null) loadingUI.SetActive(false);
                    }
                    else
                    {
                        Debug.LogError("source_from_target_m이 유효한 4x4 행렬이 아닙니다.");
                        ShowErrorFeedback("Matrix Error");
                    }
                }
                else ShowErrorFeedback("Try Again");
            }
        }
        isRegistering = false;
    }

    private static bool IsValidMatrix(float[][] matrix)
    {
        if (matrix == null || matrix.Length != 4) return false;
        for (int row = 0; row < 4; row++)
        {
            if (matrix[row] == null || matrix[row].Length != 4) return false;
            for (int col = 0; col < 4; col++)
                if (float.IsNaN(matrix[row][col]) || float.IsInfinity(matrix[row][col])) return false;
        }
        return true;
    }

    private void ShowErrorFeedback(string message)
    {
        if (loadingUI != null && loadingText != null)
        {
            if (!isRegistering && Camera.main != null)
                PlaceLoadingUIAtPose(Camera.main.transform.position, Camera.main.transform.rotation);
            loadingUI.SetActive(true);
            loadingText.text = message;
            StartCoroutine(HideUIAfterSeconds(3f));
        }
    }

    private void PlaceLoadingUIAtPose(Vector3 cameraPosition, Quaternion cameraRotation)
    {
        if (loadingUI == null) return;

        // 정합을 시작한 순간의 카메라 자세로 한 번만 배치합니다.
        // 이후에는 위치를 갱신하지 않으므로 머리가 흔들려도 문구는 고정됩니다.
        loadingUI.transform.position = cameraPosition
            + cameraRotation * (Vector3.forward * loadingUIDistanceMeters
                + Vector3.up * loadingUIVerticalOffsetMeters);
        loadingUI.transform.rotation = cameraRotation;
    }

    private IEnumerator HideUIAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (loadingUI != null) loadingUI.SetActive(false);
    }

    private void ApplyMatrix(float[][] M, bool isAlreadyMeters)
    {
        float tx = M[0][3];
        float ty = M[1][3];
        float tz = M[2][3];

        if (!isAlreadyMeters)
        {
            tx /= 1000f;
            ty /= 1000f;
            tz /= 1000f;
        }

        // 확정된 카메라 좌표계 오프셋을 서버 행렬에 더합니다.
        // X+: 오른쪽, Y+: 위, Z+: 앞쪽입니다.
        Vector3 configuredOffset = applyManualOffset ? manualOffsetMeters : Vector3.zero;
        Vector3 localPosition = new Vector3(tx, -ty, tz) + configuredOffset;

        Matrix4x4 unityMatrix = new Matrix4x4();
        unityMatrix.SetRow(0, new Vector4(-M[0][0],  M[0][1],  M[0][2],  0));
        unityMatrix.SetRow(1, new Vector4( M[1][0], -M[1][1], -M[1][2],  0));
        unityMatrix.SetRow(2, new Vector4(-M[2][0],  M[2][1],  M[2][2],  0));
        unityMatrix.SetRow(3, new Vector4(       0,        0,        0,  1));

        Quaternion localRotation = unityMatrix.rotation;

        Vector3 worldPos = captureCameraPosition + (captureCameraRotation * localPosition);
        Quaternion worldRot = captureCameraRotation * localRotation;

        targetMeshAnchor.position = worldPos;
        targetMeshAnchor.rotation = worldRot;

        PlaceSubj2UiPanelBesideRegisteredSkin();

        registeredBaseWorldPosition = worldPos;
        registeredOffsetFrameRotation = captureCameraRotation;
        temporaryOffsetMeters = Vector3.zero;
        hasRegisteredPose = true;
        registeredPatientId = patientModelLoader != null
            ? patientModelLoader.CurrentPatientId : null;

        if (temporaryCalibration != null)
            temporaryCalibration.OnRegistrationCompleted();

        Debug.Log("✅ 렌더링 업데이트 완료! 최종 월드 위치: " + targetMeshAnchor.position +
            " / 고정 오프셋 적용=" + applyManualOffset);
    }

    public void SetTemporaryCalibrationOffset(Vector3 offsetMeters)
    {
        temporaryOffsetMeters = offsetMeters;
        if (!hasRegisteredPose || targetMeshAnchor == null) return;

        // X/Y/Z 값은 정합 버튼을 눌렀을 때의 HoloLens 카메라 좌표입니다.
        // X+: 오른쪽, Y+: 위, Z+: 앞. 음수 Z는 뒤쪽입니다.
        targetMeshAnchor.position = registeredBaseWorldPosition
            + registeredOffsetFrameRotation * temporaryOffsetMeters;
    }

    public Vector3 TemporaryCalibrationOffset
    {
        get { return temporaryOffsetMeters; }
    }

    public string GetServerUrl(string path)
    {
        return patientModelLoader != null
            ? patientModelLoader.GetServerUrl(path)
            : "http://127.0.0.1:55555" + path;
    }

    private void PlaceSubj2UiPanelBesideRegisteredSkin()
    {
        if (uiPanel == null || patientModelLoader == null ||
            !string.Equals(patientModelLoader.CurrentPatientId, "subj2",
                System.StringComparison.OrdinalIgnoreCase))
            return;

        Renderer skinRenderer = patientModelLoader.skinMeshFilter != null
            ? patientModelLoader.skinMeshFilter.GetComponent<Renderer>() : null;
        if (skinRenderer == null) return;

        Bounds skinBounds = skinRenderer.bounds;
        Vector3 cameraRight = captureCameraRotation * Vector3.right;
        Vector3 absoluteRight = new Vector3(
            Mathf.Abs(cameraRight.x),
            Mathf.Abs(cameraRight.y),
            Mathf.Abs(cameraRight.z));
        float projectedHalfWidth = Vector3.Dot(absoluteRight, skinBounds.extents);

        uiPanel.position = skinBounds.center
            + cameraRight * (projectedHalfWidth + uiPanelRightOffsetFromSkinCenterMeters);
        uiPanel.rotation = captureCameraRotation;

        Debug.Log("subj2 UI 패널 보정 완료: 피부 오른쪽, 캡처 카메라 정면 방향");
    }
}

public class RegistrationResponse
{
    public string status;
    public string patient_id;
    public string unit;
    public float[][] matrix;
    public float[][] source_from_target_mm;
    public float[][] source_from_target_m;
    public float[][] hololens_from_target_m;
}
