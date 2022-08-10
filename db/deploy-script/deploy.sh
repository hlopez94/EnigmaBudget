while ! mysql --host=db --port=3306 --user=${SQL_USER} --database=${SQL_DB} --password=${SQL_ROOT_PASS} -e ";" ;
    do sleep 10
done
for filename in ./sql-scripts/*.sql
do
    mysql --host=db --port=3306 --user=${SQL_USER} --database=${SQL_DB} --password=${SQL_ROOT_PASS} < ${filename}
done