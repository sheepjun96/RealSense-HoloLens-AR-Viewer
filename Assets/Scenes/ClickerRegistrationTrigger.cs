using UnityEngine;
using UnityEngine.Events;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;

public class ClickerRegistrationTrigger : MonoBehaviour, IMixedRealityPointerHandler
{
    [Header("설정")]
    public float requiredHoldTime = 2.0f; 

    [Header("실행할 이벤트")]
    public UnityEvent onLongPressTriggered;

    private bool isPressing = false;
    private float holdTimer = 0f;
    private IMixedRealityPointer activePointer = null;

    private void OnEnable()
    {
        CoreServices.InputSystem?.RegisterHandler<IMixedRealityPointerHandler>(this);
    }

    private void OnDisable()
    {
        CoreServices.InputSystem?.UnregisterHandler<IMixedRealityPointerHandler>(this);
    }

    private void Update()
    {
        if (isPressing && activePointer != null)
        {
            if (IsPointerOverUI(activePointer))
            {
                CancelPress();
                return;
            }

            holdTimer += Time.deltaTime;

            if (holdTimer >= requiredHoldTime)
            {
                Debug.Log("[Clicker] 허공/팬텀 2초 홀드 완료! 정합 실행.");
                onLongPressTriggered?.Invoke(); 
                CancelPress(); 
            }
        }
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData)
    {
        if (IsPointerOverUI(eventData.Pointer))
        {
            return;
        }

        isPressing = true;
        holdTimer = 0f;
        activePointer = eventData.Pointer;
    }

    public void OnPointerUp(MixedRealityPointerEventData eventData)
    {
        if (activePointer == eventData.Pointer)
        {
            CancelPress();
        }
    }

    private void CancelPress()
    {
        isPressing = false;
        holdTimer = 0f;
        activePointer = null;
    }

    private bool IsPointerOverUI(IMixedRealityPointer pointer)
    {
        if (pointer == null || pointer.Result == null) return false;
        
        GameObject target = pointer.Result.CurrentPointerTarget;
        if (target == null) return false;

        Transform current = target.transform;
        
        while (current != null)
        {
            if (current.gameObject.layer == LayerMask.NameToLayer("UI"))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData) { }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { }
}