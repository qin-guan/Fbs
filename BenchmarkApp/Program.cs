using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BenchmarkApp
{
    public class User
    {
        public string Phone { get; set; }
    }

    public class Booking
    {
        public int Id { get; set; }
        public string FacilityName { get; set; }
        public string Conduct { get; set; }
        public string Description { get; set; }
        public string PocName { get; set; }
        public string PocPhone { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string UserPhone { get; set; }
    }

    public class BookingWithUser
    {
        public int Id { get; set; }
        public string FacilityName { get; set; }
        public string Conduct { get; set; }
        public string Description { get; set; }
        public string PocName { get; set; }
        public string PocPhone { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public User User { get; set; }
    }

    [MemoryDiagnoser]
    public class OptimizationBenchmark
    {
        private List<User> _users;
        private List<Booking> _bookings;
        private string _facilityName = "MainHall";
        private DateTime _reqStartTime = new DateTime(2023, 1, 1, 10, 0, 0);
        private DateTime _reqEndTime = new DateTime(2023, 1, 1, 12, 0, 0);

        [Params(100, 1000)]
        public int N { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _users = new List<User>();
            _bookings = new List<Booking>();

            for (int i = 0; i < N; i++)
            {
                string phone = $"1234{i}";
                _users.Add(new User { Phone = phone });

                _bookings.Add(new Booking
                {
                    Id = i,
                    FacilityName = _facilityName,
                    StartDateTime = new DateTime(2023, 1, 1, 10, 30, 0),
                    EndDateTime = new DateTime(2023, 1, 1, 11, 30, 0),
                    UserPhone = phone
                });
            }
        }

        [Benchmark(Baseline = true)]
        public List<BookingWithUser> Original()
        {
            return _bookings
                .Where(b => b.FacilityName == _facilityName)
                .Where(b => b.StartDateTime <= _reqEndTime && b.EndDateTime >= _reqStartTime)
                .Select(booking => new BookingWithUser
                {
                    Id = booking.Id,
                    FacilityName = booking.FacilityName,
                    Conduct = booking.Conduct,
                    Description = booking.Description,
                    PocName = booking.PocName,
                    PocPhone = booking.PocPhone,
                    StartDateTime = booking.StartDateTime,
                    EndDateTime = booking.EndDateTime,
                    User = _users.Single(u => u.Phone == booking.UserPhone),
                })
                .ToList();
        }

        [Benchmark]
        public List<BookingWithUser> Optimized()
        {
            var userDict = _users.ToDictionary(u => u.Phone);

            return _bookings
                .Where(b => b.FacilityName == _facilityName)
                .Where(b => b.StartDateTime <= _reqEndTime && b.EndDateTime >= _reqStartTime)
                .Select(booking => new BookingWithUser
                {
                    Id = booking.Id,
                    FacilityName = booking.FacilityName,
                    Conduct = booking.Conduct,
                    Description = booking.Description,
                    PocName = booking.PocName,
                    PocPhone = booking.PocPhone,
                    StartDateTime = booking.StartDateTime,
                    EndDateTime = booking.EndDateTime,
                    User = userDict[booking.UserPhone],
                })
                .ToList();
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<OptimizationBenchmark>();
        }
    }
}
