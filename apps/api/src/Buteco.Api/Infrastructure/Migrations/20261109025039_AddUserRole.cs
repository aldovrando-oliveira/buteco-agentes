using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <summary>
    /// O papel do usuário (design.md da change papeis-no-token, D4 e Migration Plan).
    /// <list type="bullet">
    /// <item><b>Todo usuário que já existe vira <c>admin</c></b>, o do bootstrap e os convites
    /// pendentes inclusive: na M1 todo usuário era "Admin em poder" (#180 D4), e ninguém perde
    /// acesso no deploy. O dono rebaixa depois, por <c>PUT /users/{id}/role</c>;</item>
    /// <item><b>depois, o default passa a <c>reader</c></b>, e fica só no banco: o modelo do EF
    /// não o declara (<c>AppDbContext</c>), e todo <c>INSERT</c> do código leva o papel. Ele
    /// existe para a volta atrás: a imagem anterior insere em <c>users</c> (convite, bootstrap)
    /// sem a coluna, e sem default o <c>NOT NULL</c> recusaria. O que ela inserir sai com o menor
    /// papel;</item>
    /// <item><b>o <c>CHECK</c></b> recusa papel fora dos três textos de <c>UserRoleText</c>.</item>
    /// </list>
    /// </summary>
    public partial class AddUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "admin");

            migrationBuilder.Sql("ALTER TABLE users ALTER COLUMN role SET DEFAULT 'reader';");
            migrationBuilder.Sql("ALTER TABLE users ADD CONSTRAINT \"CK_users_role\" CHECK (role IN ('admin', 'operator', 'reader'));");
        }

        /// <summary>
        /// Apaga a coluna e com ela os papéis. Numa volta atrás, não rodar: a imagem anterior
        /// ignora a coluna (design.md, Migration Plan).
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE users DROP CONSTRAINT \"CK_users_role\";");
            migrationBuilder.DropColumn(
                name: "role",
                table: "users");
        }
    }
}
