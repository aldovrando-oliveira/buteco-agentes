namespace Buteco.Workers.Knowledge.Execution;

/// <summary>
/// Monta a descrição da tool exposta ao modelo: a <c>Description</c> cadastrada
/// da base, mais o bloco que ensina a **ler** o resultado.
///
/// <para>
/// <b>As duas metades são necessárias por motivos diferentes.</b> A descrição da
/// base é o critério de escolha — é por ela que o modelo decide se esta base é
/// relevante, e é por isso que <c>apps/api</c> a exige não-vazia desde a etapa 1.
/// O bloco fixo é o que sustenta a decisão de não ter limiar: distância exposta
/// que o modelo não sabe ler é limiar escondido.
/// </para>
/// </summary>
public static class KnowledgeToolDescription
{
    /// <summary>
    /// Bloco fixo, igual para toda base.
    ///
    /// <para>
    /// <b>NÃO ACRESCENTAR AQUI NENHUM VALOR NUMÉRICO DE CORTE</b> — nem em prosa,
    /// nem em exemplo, nem como "normalmente acima de X". Seria o limiar que a
    /// etapa <c>0c</c> reprovou com 20 negativas, reintroduzido em prosa e sem o
    /// benefício de ser testável: em 0,42 o corte descarta 10,8% das consultas
    /// boas para barrar 85% das sem-alvo, e em 0,45 descarta 7,2% e deixa passar
    /// 30%. Nenhum valor serve, e escrever um aqui apenas moveria a decisão
    /// reprovada do código para o texto.
    /// </para>
    ///
    /// <para>
    /// O que ele afirma é tudo o que o sistema sabe, e nada além (convenção 13):
    /// que os trechos são os mais próximos, que vêm mesmo quando nenhum responde,
    /// que a distância ordena e não mede acerto, e que quem decide relevância é
    /// quem lê o trecho.
    /// </para>
    ///
    /// <para>
    /// <b>Há um guarda de asserção negativa sobre este texto</b>
    /// (<c>KnowledgeToolSetResolverTests</c>): ele reprova se aparecer aqui um
    /// dígito apresentado como corte. É o guarda contra a regressão
    /// bem-intencionada de "deixar mais útil".
    /// </para>
    /// </summary>
    private const string ComoLerOResultado =
        "Esta ferramenta devolve sempre os trechos mais próximos da sua consulta, "
      + "inclusive quando nenhum deles responde à pergunta — ela não filtra por relevância. "
      + "Cada trecho vem com uma distância: menor significa mais próximo da consulta, e ela "
      + "serve para ordenar, não para medir acerto. Para saber se um trecho responde, leia o "
      + "trecho. Se nenhum falar do que foi perguntado, diga que a base não cobre o assunto em "
      + "vez de responder a partir deles.";

    public static string Build(string knowledgeBaseName, string knowledgeBaseDescription) =>
        $"Busca trechos na base de conhecimento '{knowledgeBaseName}'. "
      + $"Conteúdo da base: {knowledgeBaseDescription} "
      + ComoLerOResultado;
}
