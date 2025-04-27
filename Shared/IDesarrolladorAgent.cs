namespace Shared;

public interface IDesarrolladorAgent
{
    Task GenerarCodigoParaTarea(Prompt prompt, string tarea);
}
