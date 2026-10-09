using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.InputSystem;
using UnityEngine.Events;
using System;

public class UI_InteractionController : MonoBehaviour
{
    [SerializeField]
    GameObject UIController;

    [SerializeField]
    GameObject BaseController;

    [SerializeField]
    InputActionReference inputActionReference_UISwitcher;

    bool isUICanvasActive = false;

    [SerializeField]
    GameObject UICanvasGameobject;


    private void OnEnable()
    {
        inputActionReference_UISwitcher.action.performed += ActivateUIMode;
    }
    private void OnDisable()
    {
        inputActionReference_UISwitcher.action.performed -= ActivateUIMode;

    }

    private void Start()
    {
        //Deactivating UI Canvas Gameobject by default
        if (UICanvasGameobject != null)
        {
            UICanvasGameobject.SetActive(false);

        }

        //Deactivating UI Controller by default
        UIController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().enabled = false;
        UIController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>().enabled = false;
    }

    /// <summary>
    /// This method is called when the player presses UI Switcher Button which is the input action defined in Default Input Actions.
    /// When it is called, UI interaction mode is switched on and off according to the previous state of the UI Canvas.
    /// </summary>
    /// <param name="obj"></param>
    private void ActivateUIMode(InputAction.CallbackContext obj)
    {
        if (!isUICanvasActive)
        {
            isUICanvasActive = true;

            //Activating UI Controller by enabling its XR Ray Interactor and XR Interactor Line Visual
            UIController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().enabled = true;
            UIController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>().enabled = true;

            //Deactivating Base Controller by disabling its XR Direct Interactor
            BaseController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>().enabled = false;



            //Activating the UI Canvas Gameobject
            UICanvasGameobject.SetActive(true);
        }
        else
        {
            isUICanvasActive = false;

            //De-Activating UI Controller by enabling its XR Ray Interactor and XR Interactor Line Visual
            UIController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().enabled = false;
            UIController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>().enabled = false;

            //Activating Base Controller by disabling its XR Direct Interactor
            BaseController.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>().enabled = true;

            //De-Activating the UI Canvas Gameobject
            UICanvasGameobject.SetActive(false);
        }

    }
}
