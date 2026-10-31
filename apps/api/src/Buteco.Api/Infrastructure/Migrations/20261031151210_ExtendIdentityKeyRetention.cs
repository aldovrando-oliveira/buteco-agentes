using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <summary>
    /// Só dados (design.md da change usuarios-locais-login-emissor, D6 e D3.7). A janela de
    /// cada chave está gravada na linha (<c>validates_until = signs_until + Retention</c>, na
    /// criação) e vira o <c>NotAfter</c> do certificado em memória. Subir a constante de 24 h
    /// para 15 dias não alcança as chaves já criadas: a ativa de hoje sairia do anel 24 h
    /// depois da primeira troca, e o refresh de 14 dias assinado por ela seria recusado.
    ///
    /// <para>
    /// Só estende, nunca encurta, e rodar de novo não muda nada. Não toca a chave de cifra,
    /// que vale até o infinito. Uma chave aposentada há menos de 15 dias volta ao anel e ao
    /// JWKS, o que só devolve o comportamento decidido. O <c>Down</c> não desfaz: encurtar
    /// recusaria refresh em voo, e a versão anterior do app convive com a janela maior.
    /// </para>
    /// </summary>
    public partial class ExtendIdentityKeyRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE identity_keys
                SET validates_until = signs_until + interval '15 days'
                WHERE usage = 'Signing'
                  AND validates_until < signs_until + interval '15 days';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
