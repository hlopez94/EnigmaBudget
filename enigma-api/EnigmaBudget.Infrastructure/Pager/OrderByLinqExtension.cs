using System.Linq.Expressions;
using System.Reflection;

namespace EnigmaBudget.Infrastructure.Pager
{
    /// https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/attributes/creating-custom-attributes
    /// https://stackoverflow.com/questions/6637679/reflection-get-attribute-name-and-value-on-property
    /// https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/attributes/accessing-attributes-by-using-reflection
    /// https://learn.microsoft.com/en-us/dotnet/standard/attributes/retrieving-information-stored-in-attributes
    /// https://stackoverflow.com/questions/39685787/how-to-make-a-dynamic-order-in-entity-framework
    /// https://nwb.one/blog/linq-extensions-pagination-order-by-property-name    /// 
    /// <summary>
    /// Extension LINQ para alterar una expresión lambda con el fin de agregar ordenado por propiedades y paginado en la búsqueda
    /// </summary>
    public static class OrderByLinqExtension
    { /// <summary>
      /// Convierte una expresion lambda en una expresión paginada y ordenable por Alias de Columna
      /// </summary>
      /// <typeparam name="T"></typeparam>
      /// <param name="src">Expresión Lambda a paginar y/o ordenar por Alias de Columna </param>
      /// <param name="pageNumber">Numero de Página a buscar (valor mayor o igual a 1)</param>
      /// <param name="pageSize">Tamaño de Página (cantidad de resultados por página)</param>
      /// <param name="orderByColumnAlias">Alias de Columna, seteado en la entidad del repositorio a través del Attributo <seealso cref="OrderColumnAliasAttribute"/> en los parámetros a traves de los que se desee ordenar. Vacío para listar resultados sin orden específico </param>
      /// <returns>Expresión LINQ paginada.</returns>
        public static IQueryable<T> ToPagedSearch<T>(this IQueryable<T> src, int pageNumber = 1, int pageSize = 10, string? orderByColumnAlias = null)  
        {
            var queryExpression = src.Expression;

            if(!string.IsNullOrWhiteSpace(orderByColumnAlias))
            {
                queryExpression = AddOrderBy<T>(queryExpression, orderByColumnAlias);
            }

            if(queryExpression.CanReduce)
                queryExpression = queryExpression.Reduce();

            src = src.Provider.CreateQuery<T>(queryExpression);

            return src.Skip((pageNumber - 1) * pageSize).Take(pageSize);
        }
        /// <summary>
        /// Modifica la expresión lambda para que sea ordenada por el alias de columna indicado en la dirección brindada ('asc' por defecto)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source">Expresión lambda a ordenar</param>
        /// <param name="orderByColumnAlias">Alias de Columna para ordenar</param>
        /// <returns></returns>
        private static Expression AddOrderBy<T>(Expression source, string orderByColumnAlias)
        {
            //TODO: Make this to accept multiple comma separated parameters

            string orderByMethodName = "OrderBy" + (orderByColumnAlias[0] == '-' ? "Descending" : string.Empty);
            var parameterExpression = Expression.Parameter(typeof(T), "p");
            var orderByExpression = BuildPropertyPathExpression<T>(parameterExpression, orderByColumnAlias);
            var orderByFuncType = typeof(Func<,>).MakeGenericType(typeof(T), orderByExpression.Type);
            var orderByLambda = Expression.Lambda(orderByFuncType, orderByExpression, new ParameterExpression[] { parameterExpression });

            source = Expression.Call(typeof(Queryable), orderByMethodName, new Type[] { typeof(T), orderByExpression.Type }, source, orderByLambda);
            return source;
        }

        /// <summary>
        /// Genera una expresión lambda para acceder al parámetro por el cual ordenar, indicado en el objeto <typeparamref name="T"/> a traves del atributo de propiedad <typeparamref name="OrderColumnAliasAttribute"/>. <br/>
        /// Arroja <seealso cref="KeyNotFoundException"/> en caso que el objeto <typeparamref name="T"/> no posea un atributo con el decorador <paramref name="columnAlias"/> seteado.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="rootExpression">Expresión lambda de parámetro que brinda el parametro/objeto a acceder </param>
        /// <param name="columnAlias">Alias de columna para generar el OrderBy</param>
        /// <returns>Una expresión lambda para acceder a una propiedad del objeto tipo <typeparamref name="T"/></returns>
        /// <exception cref="KeyNotFoundException">
        /// </exception>
        private static Expression BuildPropertyPathExpression<T>(this Expression rootExpression, string columnAlias)
        {
            var type = typeof(T);
            var parameter = Expression.Parameter(type, "p");
            PropertyInfo property;
            Expression propertyAccess;


            var props = rootExpression.Type.GetProperties();
            property = props.Where(p => p.GetCustomAttribute<OrderColumnAliasAttribute>() != null
                                       && p.GetCustomAttribute<OrderColumnAliasAttribute>().OrderAlias == columnAlias.ToLower()).FirstOrDefault();
            if(property == null)
                throw new KeyNotFoundException($"No se encontró propiedad con alias: {columnAlias}. La expresión lambda es: {rootExpression}.");

            propertyAccess = Expression.Property(rootExpression, property);
            return propertyAccess;
        }
    }
}
