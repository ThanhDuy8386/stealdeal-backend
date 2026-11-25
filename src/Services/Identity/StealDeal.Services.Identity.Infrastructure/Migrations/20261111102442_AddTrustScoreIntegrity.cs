using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustScoreIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_UserTrustScores_Score",
                table: "UserTrustScores",
                sql: "[Score] >= 0 AND [Score] <= 100");

            migrationBuilder.CreateIndex(
                name: "UX_TrustScoreEvents_OrderOutcome",
                table: "TrustScoreEvents",
                columns: new[] { "UserId", "ReferenceType", "ReferenceId", "EventType" },
                unique: true,
                filter: "[ReferenceType] = N'Order' AND [ReferenceId] IS NOT NULL AND [EventType] IN (N'PaymentSucceeded', N'OrderNoShow')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrustScoreEvents_ScoreAfter",
                table: "TrustScoreEvents",
                sql: "[ScoreAfter] >= 0 AND [ScoreAfter] <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserTrustScores_Score",
                table: "UserTrustScores");

            migrationBuilder.DropIndex(
                name: "UX_TrustScoreEvents_OrderOutcome",
                table: "TrustScoreEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrustScoreEvents_ScoreAfter",
                table: "TrustScoreEvents");

        }
    }
}
