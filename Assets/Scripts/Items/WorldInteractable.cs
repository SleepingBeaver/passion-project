using UnityEngine;

public abstract class WorldInteractable : MonoBehaviour
{
    // Configuracao base da interacao.
    [Header("Interaction")]
    [SerializeField] private string promptText = "E Interagir";
    [SerializeField] private bool requiresHold;
    [SerializeField, Min(0.05f)] private float holdDuration = 0.5f;

    // Estado interno que impede interacoes duplicadas.
    private bool isLocked;

    // Leitura publica para quem consome este interagivel.
    public string PromptText => promptText;
    public bool RequiresHold => requiresHold;
    public float HoldDuration => holdDuration;
    public string GetPromptText(PlayerInteractor interactor) => ResolvePromptText(interactor);
    public virtual bool GetRequiresHold(PlayerInteractor interactor) => requiresHold;
    public virtual float GetHoldDuration(PlayerInteractor interactor) => holdDuration;

    // Pontos de extensao para as classes filhas reagirem ao foco e ao hold.
    public virtual bool CanFocus(PlayerInteractor interactor)
    {
        return !isLocked;
    }

    public virtual bool CanInteract(PlayerInteractor interactor)
    {
        return !isLocked;
    }

    public virtual void OnFocusEnter(PlayerInteractor interactor) { }
    public virtual void OnFocusExit(PlayerInteractor interactor) { }
    public virtual void OnHoldStarted(PlayerInteractor interactor) { }
    public virtual void OnHoldCanceled(PlayerInteractor interactor) { }

    // Fluxo principal de tentativa de interacao.
    public bool TryInteract(PlayerInteractor interactor)
    {
        if (!CanInteract(interactor))
            return false;

        return PerformInteraction(interactor);
    }

    // Controle interno de bloqueio.
    protected void LockInteraction()
    {
        isLocked = true;
    }

    protected virtual string ResolvePromptText(PlayerInteractor interactor)
    {
        return promptText;
    }

    // Contrato que cada interagivel concreto precisa implementar.
    protected abstract bool PerformInteraction(PlayerInteractor interactor);
}
