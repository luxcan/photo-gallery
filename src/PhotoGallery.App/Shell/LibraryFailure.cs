using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace PhotoGallery.App.Shell;

/// <summary>
/// Whether a fault is one the library can raise in ordinary use, which the
/// screen that caught it should say rather than let close the app.
/// </summary>
/// <remarks>
/// The catch filters that ask this were each written out for a <em>file</em>
/// read, and the library is a database as well as a folder: SqliteException
/// derives from DbException, and DbUpdateException derives straight from
/// Exception, so neither was matched by any of them. A library on a drive that
/// had gone away, or a database a second copy of the app had locked, went past
/// the filter that meant to report it and into the handler in App.xaml.cs -
/// which says what happened and then lets the app close.
///
/// <para>The two derived cases come free and are meant to:
/// ObjectDisposedException is an InvalidOperationException, so a scope disposed
/// on the way out of the app stays covered, and DbUpdateConcurrencyException is
/// a DbUpdateException. What is deliberately not here is anything that means the
/// app itself is wrong - those still reach that handler, and still leave a
/// record of why.</para>
///
/// <para>This asks one question for the whole album and collection feature. The
/// filters elsewhere in the app that name only IOException are not oversights to
/// sweep up: most of them guard a read of a file on disk, where a database fault
/// is not among the things that can happen.</para>
/// </remarks>
internal static class LibraryFailure
{
    internal static bool IsExpected(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or DbException
            or DbUpdateException;
}
