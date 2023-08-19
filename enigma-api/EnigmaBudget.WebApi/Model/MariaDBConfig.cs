﻿namespace EnigmaBudget.WebApi.Model
{
    internal class MariaDBConfig
    {

        public string Server { get; set; }
        public int Port { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Database { get; set; }

        public string ConnectionString => $"Server={Server};Port={Port};user={User};Password={Password};Database={Database}";

        public string Environment { get; set; }

    }
}