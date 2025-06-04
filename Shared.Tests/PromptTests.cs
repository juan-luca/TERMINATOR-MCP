using Shared;
using Xunit;

namespace Shared.Tests;

public class PromptTests
{
    [Fact]
    public void PromptStoresTituloAndDescripcion()
    {
        var prompt = new Prompt("Hola", "Mundo");
        Assert.Equal("Hola", prompt.Titulo);
        Assert.Equal("Mundo", prompt.Descripcion);
    }
}
