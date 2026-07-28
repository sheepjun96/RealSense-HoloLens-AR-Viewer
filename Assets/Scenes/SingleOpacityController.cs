using UnityEngine;
using Microsoft.MixedReality.Toolkit.UI;

public class SingleOpacityController : MonoBehaviour
{
    [Tooltip("여기에 투명도를 조절할 대상(default)을 넣으세요.")]
    public Renderer targetRenderer;

    public void OnSliderUpdated(SliderEventData eventData)
    {
        if (targetRenderer != null)
        {
            Color c = targetRenderer.material.color;
            c.a = eventData.NewValue; 
            targetRenderer.material.color = c;
        }
    }
}