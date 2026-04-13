using System.Collections;
using UnityEngine;

public class TreeInteractable : WorldInteractable
{
    // Configuracao da quebra da arvore.
    [Header("Tree")]
    [SerializeField] private ResourceNodeDropper resourceDropper;
    [SerializeField] private Animator animator;
    [SerializeField] private string breakTriggerName = "Break";
    [SerializeField] private float breakDelayAfterComplete = 0.1f;
    [SerializeField] private Collider2D[] collidersToDisable;

    // Fluxo de interacao disparado pelo jogador.
    protected override bool PerformInteraction(PlayerInteractor interactor)
    {
        LockInteraction();

        if (collidersToDisable != null)
        {
            for (int i = 0; i < collidersToDisable.Length; i++)
            {
                if (collidersToDisable[i] != null)
                    collidersToDisable[i].enabled = false;
            }
        }

        if (animator != null && !string.IsNullOrWhiteSpace(breakTriggerName))
            animator.SetTrigger(breakTriggerName);

        StartCoroutine(BreakRoutine());
        return true;
    }

    // Sequencia final de quebra e spawn dos recursos.
    private IEnumerator BreakRoutine()
    {
        yield return new WaitForSeconds(breakDelayAfterComplete);

        if (resourceDropper != null)
            resourceDropper.BreakNode();
        else
            Destroy(gameObject);
    }
}
