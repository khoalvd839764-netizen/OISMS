using System;

namespace OISM.Application.Features_Branches
{
    public class BranchResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string Phone { get; set; }
    }
}