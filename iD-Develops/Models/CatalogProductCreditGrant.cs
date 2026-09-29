using System.ComponentModel.DataAnnotations.Schema;
using iD_Develops.Enums;

namespace iD_Develops.Models
{
    [Table("CatalogProductCreditGrants")]
    public class CatalogProductCreditGrant
    {
        public int Id { get; set; }
        public int CatalogProductId { get; set; }
        public CatalogProduct CatalogProduct { get; set; } = null!;
        public int CreditTypeId { get; set; }
        public CreditType CreditType { get; set; } = null!;
        public int Quantity { get; set; } = 1;
        public int? ValidityValue { get; set; }
        public CreditValidityUnit? ValidityUnit { get; set; }
        public CreditGrantScope Scope { get; set; } = CreditGrantScope.Global;
        public int? CourseId { get; set; }
        public Course? Course { get; set; }
        public int? CourseClassId { get; set; }
        public CourseClass? CourseClass { get; set; }
    }
}
