package sg.from.fbs.repository

abstract class BaseRepository<T> {
    abstract fun getList(): List<T>
}