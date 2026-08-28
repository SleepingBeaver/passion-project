using UnityEngine;

// Base opcional para novas fontes criadas como componentes Unity. Ela cuida do registro
// e desregistro; a classe concreta implementa apenas resolucao e execucao do seu dominio.
public abstract class PlayerInteractionSourceBehaviour : MonoBehaviour, IPlayerInteractionSource
{
    [SerializeField] private PlayerInteractor playerInteractor;

    public PlayerInteractor Interactor => playerInteractor;
    public abstract string SourceId { get; }

    protected virtual void Reset()
    {
        playerInteractor = GetComponentInParent<PlayerInteractor>();
    }

    protected virtual void Awake()
    {
        ResolveInteractor();
    }

    protected virtual void OnEnable()
    {
        ResolveInteractor();
        playerInteractor?.RegisterInteractionSource(this);
    }

    protected virtual void OnDisable()
    {
        playerInteractor?.UnregisterInteractionSource(this);
    }

    public abstract void Refresh(PlayerInteractor interactor);

    public abstract bool TryGetCandidate(
        PlayerInteractor interactor,
        PlayerInteractionInput input,
        out PlayerInteractionCandidate candidate);

    public abstract bool TryPerform(
        PlayerInteractor interactor,
        PlayerInteractionCandidate candidate,
        out object payload);

    private void ResolveInteractor()
    {
        if (playerInteractor != null)
            return;

        playerInteractor = GetComponentInParent<PlayerInteractor>();
        playerInteractor ??= FindAnyObjectByType<PlayerInteractor>();
    }
}
