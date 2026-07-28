using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class RegistrationManager : MonoBehaviour
{
    private string apiUrl = "http://172.20.10.2:55555/hololens/capture-register";
    
    public Transform targetMeshAnchor;
    public GameObject loadingUI; 

    public void OnRegistrationButtonClicked()
    {
        Debug.Log("정합 버튼 클릭됨. API 요청 시작...");
        if (loadingUI != null) loadingUI.SetActive(true);
        StartCoroutine(SendRegistrationRequest());
    }

    private IEnumerator SendRegistrationRequest()
    {
        string jsonData = "{}"; 

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (loadingUI != null) loadingUI.SetActive(false);

            if (request.isNetworkError || request.isHttpError)
            {
                Debug.LogError("API 호출 에러: " + request.error);
            }
            else
            {
                string responseText = request.downloadHandler.text;
                Debug.Log("API 응답 완료! " + responseText); 
                
                var response = JsonConvert.DeserializeObject<RegistrationResponse>(responseText);
                
                if ((response.status == "completed" || response.status == "available" || response.status == "captured") && response.matrix != null)
                {
                    bool isAlreadyMeters = (response.unit == "meter");
                    ApplyMatrix(response.matrix, isAlreadyMeters);
                }
                else
                {
                    Debug.LogWarning("정합은 완료되었으나 행렬 데이터를 받지 못했습니다.");
                }
            }
        }
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

        Matrix4x4 unityMatrix = new Matrix4x4();
        unityMatrix.SetRow(0, new Vector4(-M[0][0],  M[0][1],  M[0][2],  tx));
        unityMatrix.SetRow(1, new Vector4( M[1][0], -M[1][1], -M[1][2], -ty));
        unityMatrix.SetRow(2, new Vector4(-M[2][0],  M[2][1],  M[2][2],  tz));
        unityMatrix.SetRow(3, new Vector4(       0,        0,        0,   1));

        targetMeshAnchor.position = unityMatrix.GetColumn(3);
        targetMeshAnchor.rotation = unityMatrix.rotation;
        
        Debug.Log($"✅ 렌더링 업데이트 완료! 위치: {targetMeshAnchor.position}, 회전: {targetMeshAnchor.rotation.eulerAngles}");
    }
}

public class RegistrationResponse 
{
    public string status;
    public string unit; 
    public float[][] matrix; 
}