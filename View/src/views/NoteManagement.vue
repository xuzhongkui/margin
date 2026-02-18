<script setup>
import { ref, onMounted, computed, nextTick } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  Search,
  Plus,
  Edit,
  Delete,
  Refresh,
  Star,
  StarFilled,
} from "@element-plus/icons-vue";
import {
  getAllNotes,
  searchNotes,
  createNote,
  updateNote,
  deleteNote,
  togglePinNote,
} from "../services/notes";

// 富文本编辑器（使用 Quill）
import { QuillEditor } from "@vueup/vue-quill";
import "@vueup/vue-quill/dist/vue-quill.snow.css";

// 记事本列表数据
const notesList = ref([]);
// 当前选中的记事本
const currentNote = ref(null);
// 搜索关键词
const searchKeyword = ref("");
// 加载状态
const loading = ref(false);
// 编辑器内容
const editorContent = ref("");
// 编辑器标题
const editorTitle = ref("");
// 是否是新建模式
const isNewMode = ref(false);
// 是否正在编辑
const isEditing = ref(false);
// 编辑器实例引用
const quillEditor = ref(null);

// 编辑器配置
const editorOptions = {
  theme: "snow",
  modules: {
    toolbar: [
      ["bold", "italic", "underline", "strike"],
      ["blockquote", "code-block"],
      [{ header: 1 }, { header: 2 }],
      [{ list: "ordered" }, { list: "bullet" }],
      [{ script: "sub" }, { script: "super" }],
      [{ indent: "-1" }, { indent: "+1" }],
      [{ direction: "rtl" }],
      [{ size: ["small", false, "large", "huge"] }],
      [{ header: [1, 2, 3, 4, 5, 6, false] }],
      [{ color: [] }, { background: [] }],
      [{ font: [] }],
      [{ align: [] }],
      ["clean"],
      ["link", "image"],
    ],
  },
  placeholder: "开始编写你的记事本内容...",
};

// 格式化日期时间
const formatDateTime = (dateString) => {
  if (!dateString) return "";
  const date = new Date(dateString);
  return date.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
};

// 加载记事本列表
const loadNotes = async () => {
  loading.value = true;
  try {
    const notes = await getAllNotes();
    notesList.value = notes;
  } catch (error) {
    ElMessage.error("加载记事本列表失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 搜索记事本
const handleSearch = async () => {
  loading.value = true;
  try {
    const notes = await searchNotes(searchKeyword.value);
    notesList.value = notes;
    ElMessage.success(`找到 ${notes.length} 条记事本`);
  } catch (error) {
    ElMessage.error("搜索失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 刷新列表
const handleRefresh = async () => {
  searchKeyword.value = "";
  await loadNotes();
  ElMessage.success("刷新成功");
};

// 选择记事本
const selectNote = (note) => {
  if (isEditing.value) {
    ElMessageBox.confirm("当前有未保存的内容，是否放弃？", "提示", {
      confirmButtonText: "放弃",
      cancelButtonText: "取消",
      type: "warning",
    })
      .then(() => {
        loadNoteContent(note);
      })
      .catch(() => {});
  } else {
    loadNoteContent(note);
  }
};

// 加载记事本内容
const loadNoteContent = (note) => {
  currentNote.value = note;
  editorTitle.value = note.title;
  editorContent.value = note.content;
  isNewMode.value = false;
  isEditing.value = false;
};

// 新建记事本
const handleAdd = () => {
  if (isEditing.value) {
    ElMessageBox.confirm("当前有未保存的内容，是否放弃？", "提示", {
      confirmButtonText: "放弃",
      cancelButtonText: "取消",
      type: "warning",
    })
      .then(() => {
        createNewNote();
      })
      .catch(() => {});
  } else {
    createNewNote();
  }
};

const createNewNote = async () => {
  currentNote.value = null;
  isNewMode.value = true;
  isEditing.value = true;

  // 使用 nextTick 确保 DOM 更新后再清空内容
  await nextTick();
  editorTitle.value = "";
  editorContent.value = "";

  // 直接操作 Quill 编辑器实例清空内容
  if (quillEditor.value && quillEditor.value.getQuill) {
    quillEditor.value.getQuill().setText("");
  }
};

// 编辑当前记事本
const handleEdit = () => {
  if (!currentNote.value) {
    ElMessage.warning("请先选择一条记事本");
    return;
  }
  isEditing.value = true;
};

// 保存记事本
const handleSave = async () => {
  if (!editorTitle.value.trim()) {
    ElMessage.warning("请输入标题");
    return;
  }

  loading.value = true;
  try {
    const noteData = {
      title: editorTitle.value,
      content: editorContent.value,
      userId: null,
      tags: null,
      isPinned: false,
      remark: null,
    };

    if (isNewMode.value) {
      // 创建新记事本
      const newNote = await createNote(noteData);
      ElMessage.success("创建成功");
      await loadNotes();
      // 选中新创建的记事本
      const found = notesList.value.find((n) => n.id === newNote.id);
      if (found) {
        loadNoteContent(found);
      }
    } else if (currentNote.value) {
      // 更新记事本
      noteData.isPinned = currentNote.value.isPinned;
      await updateNote(currentNote.value.id, noteData);
      ElMessage.success("保存成功");
      await loadNotes();
      // 重新加载当前记事本
      const found = notesList.value.find((n) => n.id === currentNote.value.id);
      if (found) {
        loadNoteContent(found);
      }
    }
    isEditing.value = false;
  } catch (error) {
    ElMessage.error("保存失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 取消编辑
const handleCancel = () => {
  if (isNewMode.value) {
    editorTitle.value = "";
    editorContent.value = "";
    currentNote.value = null;
    isNewMode.value = false;
  } else if (currentNote.value) {
    editorTitle.value = currentNote.value.title;
    editorContent.value = currentNote.value.content;
  }
  isEditing.value = false;
};

// 删除记事本
const handleDelete = async () => {
  if (!currentNote.value) {
    ElMessage.warning("请先选择一条记事本");
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定要删除记事本 "${currentNote.value.title}" 吗？此操作不可恢复。`,
      "删除确认",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning",
      }
    );

    loading.value = true;
    await deleteNote(currentNote.value.id);
    ElMessage.success("删除成功");
    currentNote.value = null;
    editorTitle.value = "";
    editorContent.value = "";
    isNewMode.value = false;
    isEditing.value = false;
    await loadNotes();
  } catch (error) {
    if (error !== "cancel") {
      ElMessage.error("删除失败: " + error.message);
    }
  } finally {
    loading.value = false;
  }
};

// 切换置顶
const handleTogglePin = async (note) => {
  loading.value = true;
  try {
    await togglePinNote(note.id);
    ElMessage.success(note.isPinned ? "取消置顶成功" : "置顶成功");
    await loadNotes();
    // 如果是当前选中的记事本，更新状态
    if (currentNote.value && currentNote.value.id === note.id) {
      const found = notesList.value.find((n) => n.id === note.id);
      if (found) {
        currentNote.value = found;
      }
    }
  } catch (error) {
    ElMessage.error("操作失败: " + error.message);
  } finally {
    loading.value = false;
  }
};

// 监听内容变化
const onContentChange = () => {
  if (!isEditing.value && !isNewMode.value) {
    isEditing.value = true;
  }
};

onMounted(() => {
  loadNotes();
});
</script>

<template>
  <div class="note-management">
    <div class="content-wrapper">
      <!-- 左侧记事本列表 -->
      <div class="left-panel">
        <el-card shadow="never" class="search-card">
          <el-input
            v-model="searchKeyword"
            placeholder="搜索记事本"
            clearable
            @keyup.enter="handleSearch"
          >
            <template #prefix>
              <el-icon><Search /></el-icon>
            </template>
          </el-input>
          <div class="action-buttons">
            <el-button
              type="primary"
              :icon="Search"
              @click="handleSearch"
              size="small"
            >
              搜索
            </el-button>
            <el-button :icon="Refresh" @click="handleRefresh" size="small"
              >刷新</el-button
            >
            <el-button
              type="success"
              :icon="Plus"
              @click="handleAdd"
              size="small"
            >
              新建
            </el-button>
          </div>
        </el-card>

        <el-card shadow="never" class="list-card" v-loading="loading">
          <div class="notes-list">
            <div
              v-for="note in notesList"
              :key="note.id"
              class="note-item"
              :class="{ active: currentNote && currentNote.id === note.id }"
              @click="selectNote(note)"
            >
              <div class="note-header">
                <span class="note-title">{{ note.title }}</span>
                <el-icon
                  class="pin-icon"
                  :class="{ pinned: note.isPinned }"
                  @click.stop="handleTogglePin(note)"
                >
                  <StarFilled v-if="note.isPinned" />
                  <Star v-else />
                </el-icon>
              </div>
              <div class="note-time">{{ formatDateTime(note.updateTime) }}</div>
              <div
                class="note-preview"
                v-html="note.content.substring(0, 100)"
              ></div>
            </div>
            <el-empty v-if="notesList.length === 0" description="暂无记事本" />
          </div>
        </el-card>
      </div>

      <!-- 右侧编辑区 -->
      <div class="right-panel">
        <el-card shadow="never" class="editor-card">
          <template #header>
            <div class="editor-header">
              <el-input
                v-model="editorTitle"
                placeholder="请输入标题"
                :disabled="!isEditing && !isNewMode"
                class="title-input"
                @input="onContentChange"
              />
              <div class="editor-actions">
                <el-button
                  v-if="!isEditing && !isNewMode"
                  type="primary"
                  :icon="Edit"
                  @click="handleEdit"
                  :disabled="!currentNote"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="isEditing || isNewMode"
                  type="success"
                  @click="handleSave"
                  :loading="loading"
                >
                  保存
                </el-button>
                <el-button v-if="isEditing || isNewMode" @click="handleCancel">
                  取消
                </el-button>
                <el-button
                  v-if="!isNewMode"
                  type="danger"
                  :icon="Delete"
                  @click="handleDelete"
                  :disabled="!currentNote"
                >
                  删除
                </el-button>
              </div>
            </div>
          </template>

          <div class="editor-content">
            <QuillEditor
              ref="quillEditor"
              v-model:content="editorContent"
              :options="editorOptions"
              :disabled="!isEditing && !isNewMode"
              content-type="html"
              @update:content="onContentChange"
            />
          </div>
        </el-card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.note-management {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.content-wrapper {
  flex: 1;
  display: flex;
  gap: 16px;
  padding: 24px;
  overflow: hidden;
}

.left-panel {
  width: 350px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  overflow: hidden;
}

.right-panel {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.search-card {
  flex-shrink: 0;
}

.action-buttons {
  margin-top: 12px;
  display: flex;
  gap: 8px;
}

.list-card {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.list-card :deep(.el-card__body) {
  flex: 1;
  overflow: hidden;
  padding: 0;
}

.notes-list {
  height: 100%;
  overflow-y: auto;
  padding: 12px;
}

.note-item {
  padding: 12px;
  border-bottom: 1px solid #ebeef5;
  cursor: pointer;
  transition: background-color 0.3s;
}

.note-item:hover {
  background-color: #f5f7fa;
}

.note-item.active {
  background-color: #ecf5ff;
  border-left: 3px solid #409eff;
}

.note-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.note-title {
  font-weight: bold;
  font-size: 14px;
  color: #303133;
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pin-icon {
  font-size: 16px;
  color: #909399;
  cursor: pointer;
  transition: color 0.3s;
}

.pin-icon:hover {
  color: #409eff;
}

.pin-icon.pinned {
  color: #f56c6c;
}

.note-time {
  font-size: 12px;
  color: #909399;
  margin-bottom: 8px;
}

.note-preview {
  font-size: 12px;
  color: #606266;
  line-height: 1.5;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.note-preview :deep(*) {
  margin: 0;
  padding: 0;
  font-size: inherit;
  line-height: inherit;
}

.editor-card {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.editor-card :deep(.el-card__body) {
  flex: 1;
  overflow: hidden;
  padding: 0;
}

.editor-header {
  display: flex;
  align-items: center;
  gap: 12px;
}

.title-input {
  flex: 1;
}

.title-input :deep(.el-input__wrapper) {
  font-size: 18px;
  font-weight: bold;
}

.editor-actions {
  display: flex;
  gap: 8px;
}

.editor-content {
  height: 100%;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.editor-content :deep(.quill-editor) {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.editor-content :deep(.ql-toolbar) {
  border: none;
  border-bottom: 1px solid #ebeef5;
  background-color: #f5f7fa;
}

.editor-content :deep(.ql-container) {
  flex: 1;
  border: none;
  overflow-y: auto;
  font-size: 14px;
}

.editor-content :deep(.ql-editor) {
  padding: 20px;
  min-height: 100%;
}

.editor-content :deep(.ql-editor.ql-blank::before) {
  font-style: normal;
  color: #c0c4cc;
}
</style>
