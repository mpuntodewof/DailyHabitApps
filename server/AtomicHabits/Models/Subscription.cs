namespace AtomicHabits.Models
{
    public enum PlanTier { Free = 0, Pro = 1 }

    // None = never subscribed; Active = paid & current; PastDue = payment failed
    // but in grace; Canceled = ended. Only Active grants Pro access.
    public enum SubscriptionStatus { None = 0, Active = 1, PastDue = 2, Canceled = 3 }
}
