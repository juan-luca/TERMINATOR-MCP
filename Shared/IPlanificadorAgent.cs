namespace Shared;

public interface IPlanificadorAgent
{
    Task<string[]> ConvertirPromptABacklog(Prompt prompt);
}

