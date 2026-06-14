namespace Aedis.Database.Abstractions;

public enum ReadReplicaStrategy
{
    RoundRobin,
    Random,
    LeastConnections
}