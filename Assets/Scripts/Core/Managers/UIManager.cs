using UnityEngine;
using TMPro;
using System.Collections;

public class UIManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject readyText;
    [SerializeField] private TMP_Text gameOverText;
    [SerializeField] private TMP_Text highScoreText;
    [SerializeField] private TMP_Text currentRoundText;
    [SerializeField] private TMP_Text bestRoundText;

    [Header("Players")]
    [SerializeField] private GameObject[] playerTextGroups;
    [SerializeField] private TMP_Text[] scoreTexts;
    [SerializeField] private CanvasGroup[] playerScoresTitle;

    private Coroutine[] flickerCoroutines;
    private int currentPlayerCount = 1;

    private void Awake()
    {
        flickerCoroutines = new Coroutine[playerScoresTitle.Length];
    }

    public void InitializeUI(int playerCount)
    {
        currentPlayerCount = Mathf.Clamp(playerCount, 1, scoreTexts.Length);
        SetScorePanelVisible(currentPlayerCount);
        HidePlayerIntroText();
        ShowReadyText(false);
        ShowGameOverText(false);
    }

    public void ShowReadyText(bool visible) => readyText?.SetActive(visible);

    public void ShowGameOverText(bool visible) => gameOverText?.gameObject.SetActive(visible);

    public void SetScore(int playerIndex, int score)
    {
        if (IsValidPlayerIndex(playerIndex) && scoreTexts[playerIndex] != null)
            scoreTexts[playerIndex].text = score.ToString("D2");
    }

    public void UpdateScores(int[] scores)
    {
        for (int i = 0; i < scoreTexts.Length && i < scores.Length; i++)
        {
            if (scoreTexts[i] != null)
                scoreTexts[i].text = scores[i].ToString("D2");
        }
    }

    public void UpdateHighScore(int score)
    {
        if (highScoreText != null)
        {
            highScoreText.text = score.ToString("D2");
            highScoreText.gameObject.SetActive(score > 0);
        }
    }

    public void UpdateIntroText(int currentPlayerIndex)
    {
        for (int i = 0; i < playerTextGroups.Length; i++)
        {
            bool shouldShow = (i == currentPlayerIndex) && (i < currentPlayerCount);
            if (playerTextGroups[i] != null)
                playerTextGroups[i].gameObject.SetActive(shouldShow);
        }
    }

    public void UpdateCurrentRound(int currentRound)
    {
        currentRoundText.text = currentRound.ToString("D2");
    }

    public void UpdateBestRound(int bestRound)
    {
        // Actualiza el texto o los elementos que muestran la mejor ronda
        bestRoundText.text = bestRound.ToString("D2");
    }

    public void HidePlayerIntroText()
    {
        foreach (var group in playerTextGroups)
        {
            if (group != null)
                group.gameObject.SetActive(false);
        }
    }

    public void SetScorePanelVisible(int playerCount)
    {
        for (int i = 0; i < scoreTexts.Length; i++)
        {
            if (scoreTexts[i] != null)
                scoreTexts[i].gameObject.SetActive(i < playerCount);
        }
    }

    public void StartPlayerFlicker(int playerIndex, float interval = 0.25f)
    {
        if (!IsValidPlayerIndex(playerIndex)) return;

        if (playerScoresTitle[playerIndex] != null)
            playerScoresTitle[playerIndex].gameObject.SetActive(true);

        StopPlayerFlicker(playerIndex);
        flickerCoroutines[playerIndex] = StartCoroutine(FadeFlickerLoop(playerScoresTitle[playerIndex], interval));
    }

    public void StopPlayerFlicker(int playerIndex)
    {
        if (!IsValidPlayerIndex(playerIndex)) return;

        if (flickerCoroutines[playerIndex] != null)
        {
            StopCoroutine(flickerCoroutines[playerIndex]);
            flickerCoroutines[playerIndex] = null;
        }

        if (playerScoresTitle[playerIndex] != null)
            playerScoresTitle[playerIndex].alpha = 1f;
    }

    private IEnumerator FadeFlickerLoop(CanvasGroup group, float interval)
    {
        if (group == null) yield break;

        bool fadingOut = false;

        while (true)
        {
            group.alpha = fadingOut ? 0f : 1f;
            fadingOut = !fadingOut;
            yield return new WaitForSeconds(interval);
        }
    }

    private bool IsValidPlayerIndex(int index)
    {
        return index >= 0 && index < playerScoresTitle.Length;
    }
}