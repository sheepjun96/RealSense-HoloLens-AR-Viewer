using System;
using System.Collections;
using Microsoft.MixedReality.Toolkit.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

// --demo 서버의 웹 토글로 표시 여부를 제어하는 HoloLens용 XYZ 오프셋 도구입니다.
// 슬라이더는 UI_Panel의 자식이라 환자 옆 위치를 그대로 따라갑니다.
public class TemporaryOffsetCalibration : MonoBehaviour
{
    private const float RangeMeters = 0.50f;
    private const int StepDivisions = 100; // 1 cm 간격
    private const float ConfigPollingIntervalSeconds = 3f;

    private RegistrationManager registrationManager;
    private Transform patientUiPanel;
    private GameObject labelTemplate;
    private GameObject calibrationPanel;
    private readonly TextMeshPro[] valueLabels = new TextMeshPro[3];
    private readonly PinchSlider[] sliders = new PinchSlider[3];
    private Vector3 offsetMeters;
    private bool initialized;
    private bool demoModeEnabled;
    private bool hasRegisteredPose;
    private bool panelVisible;

    [Serializable]
    private class RuntimeConfigResponse
    {
        public bool demo_mode;
        public bool temporary_offset_ui_enabled;
        public float poll_interval_seconds;
    }

    public void Initialize(
        RegistrationManager manager,
        Transform uiPanel,
        GameObject loadingUiTemplate)
    {
        if (initialized) return;

        registrationManager = manager;
        patientUiPanel = uiPanel;
        labelTemplate = loadingUiTemplate;

        PinchSlider template = FindSliderTemplate();
        if (template == null)
        {
            Debug.LogError("TemporaryOffsetCalibration: 복제할 MRTK PinchSlider를 찾지 못했습니다.");
            return;
        }

        calibrationPanel = new GameObject("TEMP_XYZ_Offset_Calibration");
        calibrationPanel.transform.SetParent(patientUiPanel, false);
        calibrationPanel.transform.localPosition = Vector3.zero;
        calibrationPanel.transform.localRotation = Quaternion.identity;
        calibrationPanel.transform.localScale = Vector3.one;
        calibrationPanel.SetActive(false);

        // 기존 투명도 슬라이더의 왼쪽에 X/Y/Z를 세로로 나란히 배치합니다.
        float[] xPositions = { -0.24f, -0.12f, 0f };
        for (int axis = 0; axis < 3; axis++)
        {
            CreateSlider(template, axis, xPositions[axis]);
            CreateValueLabel(axis, xPositions[axis]);
        }

        initialized = true;
        StartCoroutine(PollRuntimeConfig());
    }

    public void BeginRegistration()
    {
        hasRegisteredPose = false;
        panelVisible = false;
        if (calibrationPanel != null) calibrationPanel.SetActive(false);
        RefreshPanelVisibility();
    }

    public void OnRegistrationCompleted()
    {
        if (!initialized) return;

        hasRegisteredPose = true;
        panelVisible = demoModeEnabled;
        offsetMeters = Vector3.zero;
        ResetSlidersAndLabels();
        RefreshPanelVisibility();
    }

    public void ResetForPatientChange()
    {
        hasRegisteredPose = false;
        panelVisible = false;
        offsetMeters = Vector3.zero;
        if (calibrationPanel != null) calibrationPanel.SetActive(false);
    }

    private IEnumerator PollRuntimeConfig()
    {
        while (true)
        {
            string url = registrationManager != null
                ? registrationManager.GetServerUrl("/api/runtime-config")
                : "http://127.0.0.1:55555/api/runtime-config";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();
                if (!request.isNetworkError && !request.isHttpError)
                {
                    RuntimeConfigResponse config = null;
                    try
                    {
                        config = JsonUtility.FromJson<RuntimeConfigResponse>(
                            request.downloadHandler.text);
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning("런타임 설정 JSON 해석 실패: " + error.Message);
                    }
                    if (config != null)
                        SetDemoMode(config.demo_mode && config.temporary_offset_ui_enabled);
                }
            }
            yield return new WaitForSeconds(ConfigPollingIntervalSeconds);
        }
    }

    private void SetDemoMode(bool enabled)
    {
        if (demoModeEnabled == enabled) return;
        demoModeEnabled = enabled;

        if (!demoModeEnabled)
        {
            // 웹 토글 OFF는 조절 UI만 숨깁니다. 현재 offsetMeters와
            // RegistrationManager에 적용된 위치는 그대로 유지합니다.
            panelVisible = false;
            if (calibrationPanel != null) calibrationPanel.SetActive(false);
        }
        else
        {
            panelVisible = hasRegisteredPose;
        }

        RefreshPanelVisibility();
        Debug.Log("웹 XYZ 오프셋 토글: " + (demoModeEnabled ? "ON" : "OFF") +
            " / 현재 오프셋 유지: " + offsetMeters.ToString("F3") + " m");
    }

    private void RefreshPanelVisibility()
    {
        panelVisible = demoModeEnabled && hasRegisteredPose;
        if (calibrationPanel != null) calibrationPanel.SetActive(panelVisible);
    }

    private PinchSlider FindSliderTemplate()
    {
        if (patientUiPanel == null) return null;
        PinchSlider[] candidates = patientUiPanel.GetComponentsInChildren<PinchSlider>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].gameObject.name == "skin_Slider") return candidates[i];
        }
        return candidates.Length > 0 ? candidates[0] : null;
    }

    private void CreateSlider(PinchSlider template, int axis, float xPosition)
    {
        GameObject clone = Instantiate(template.gameObject, calibrationPanel.transform, false);
        clone.name = "TEMP_" + AxisName(axis) + "_Offset_Slider";

        Transform cloneTransform = clone.transform;
        cloneTransform.localPosition = new Vector3(
            xPosition, 0.12f, template.transform.localPosition.z);
        cloneTransform.localRotation = template.transform.localRotation;
        cloneTransform.localScale = template.transform.localScale;

        PinchSlider slider = clone.GetComponent<PinchSlider>();
        if (slider == null)
        {
            Debug.LogError("TemporaryOffsetCalibration: 복제된 오브젝트에 PinchSlider가 없습니다.");
            Destroy(clone);
            return;
        }

        // 복제본에 딸려온 투명도 이벤트를 버리고 위치 보정 이벤트만 연결합니다.
        slider.OnValueUpdated = new SliderEvent();
        slider.UseSliderStepDivisions = true;
        slider.SliderStepDivisions = StepDivisions;
        slider.SliderValue = 0.5f;
        int capturedAxis = axis;
        slider.OnValueUpdated.AddListener(
            eventData => OnSliderChanged(capturedAxis, eventData.NewValue));

        SingleOpacityController opacityController = clone.GetComponent<SingleOpacityController>();
        if (opacityController != null) Destroy(opacityController);

        sliders[axis] = slider;
    }

    private void CreateValueLabel(int axis, float xPosition)
    {
        if (labelTemplate == null) return;

        GameObject labelObject = Instantiate(labelTemplate, calibrationPanel.transform, false);
        labelObject.name = "TEMP_" + AxisName(axis) + "_Offset_Value";
        labelObject.SetActive(true);
        labelObject.transform.localPosition = new Vector3(xPosition, 0.27f, 0.1f);
        labelObject.transform.localRotation = Quaternion.identity;
        labelObject.transform.localScale = Vector3.one * 0.10f;

        TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
        if (label != null)
        {
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            valueLabels[axis] = label;
            UpdateLabel(axis);
        }
    }

    private void OnSliderChanged(int axis, float sliderValue)
    {
        if (!demoModeEnabled || !hasRegisteredPose) return;

        float meters = (sliderValue - 0.5f) * RangeMeters * 2f;
        if (axis == 0) offsetMeters.x = meters;
        else if (axis == 1) offsetMeters.y = meters;
        else offsetMeters.z = meters;

        UpdateLabel(axis);
        if (registrationManager != null)
            registrationManager.SetTemporaryCalibrationOffset(offsetMeters);

        Debug.Log("임시 오프셋: X=" + offsetMeters.x.ToString("+0.00;-0.00;0.00") +
            " m, Y=" + offsetMeters.y.ToString("+0.00;-0.00;0.00") +
            " m, Z=" + offsetMeters.z.ToString("+0.00;-0.00;0.00") + " m");
    }

    private void ResetSlidersAndLabels()
    {
        for (int axis = 0; axis < sliders.Length; axis++)
        {
            if (sliders[axis] != null) sliders[axis].SliderValue = 0.5f;
            UpdateLabel(axis);
        }
    }

    private void UpdateLabel(int axis)
    {
        if (axis < 0 || axis >= valueLabels.Length || valueLabels[axis] == null) return;
        float value = axis == 0 ? offsetMeters.x : axis == 1 ? offsetMeters.y : offsetMeters.z;
        valueLabels[axis].text = AxisName(axis) + " " +
            value.ToString("+0.00;-0.00;0.00") + "m";
    }

    private static string AxisName(int axis)
    {
        return axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
    }

    private void OnDestroy()
    {
        if (calibrationPanel != null) Destroy(calibrationPanel);
    }
}
