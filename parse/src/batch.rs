pub struct Batch {
    // The maximum number of entries
    capacity: usize,

    max_features: usize,

    stm_feature_buffer: Box<[i64]>,
    nstm_feature_buffer: Box<[i64]>,
    values: Box<[f32]>,
    total_features: usize,

    cp: Box<[f32]>,
    wdl: Box<[f32]>,

    // The number of entries actually written
    entries: usize,
}

impl Batch {
    pub fn new(capacity: usize, max_features: usize) -> Self {
        Self {
            capacity,
            max_features,
            stm_feature_buffer: vec![0; capacity * max_features].into_boxed_slice(),
            nstm_feature_buffer: vec![0; capacity * max_features].into_boxed_slice(),
            total_features: 0,
            values: vec![1.0; capacity * max_features].into_boxed_slice(),
            cp: vec![0_f32; capacity].into_boxed_slice(),
            wdl: vec![0_f32; capacity].into_boxed_slice(),
            entries: 0,
        }
    }

    pub fn make_entry(&mut self, cp: f32, wdl: f32) -> EntryFeatureWriter {
        let index_in_batch = self.entries;
        self.entries += 1;
        self.cp[index_in_batch] = cp;
        self.wdl[index_in_batch] = wdl;
        EntryFeatureWriter {
            batch: self,
            index_in_batch,
        }
    }

    pub fn clear(&mut self) {
        self.entries = 0;
        self.total_features = 0;
    }

    pub fn capacity(&self) -> usize {
        self.capacity
    }

    pub fn len(&self) -> usize {
        self.entries
    }

    pub fn stm_feature_buffer_ptr(&self) -> *const i64 {
        &self.stm_feature_buffer[0]
    }

    pub fn nstm_feature_buffer_ptr(&self) -> *const i64 {
        &self.nstm_feature_buffer[0]
    }

    pub fn values_ptr(&self) -> *const f32 {
        &self.values[0]
    }

    pub fn total_features(&self) -> usize {
        self.total_features
    }

    pub fn cp_ptr(&self) -> *const f32 {
        &self.cp[0]
    }

    pub fn wdl_ptr(&self) -> *const f32 {
        &self.wdl[0]
    }
}

pub struct SparseBatchWriter<'b> {
    entry_feature_writer: EntryFeatureWriter<'b>,
}

impl SparseBatchWriter<'_> {
    pub fn add_feature(&mut self, stm_feature: i64, nstm_feature: i64) {
        self.entry_feature_writer
            .add_feature_sparse(stm_feature, nstm_feature);
    }
}

pub struct CudaBatchWriter<'b> {
    entry_feature_writer: EntryFeatureWriter<'b>,
    count: usize,
}

impl CudaBatchWriter<'_> {
    pub fn add_feature(&mut self, stm_feature: i64, nstm_feature: i64) {
        self.entry_feature_writer
            .add_feature_cuda(stm_feature, nstm_feature);
    }
}

impl<'b> Drop for CudaBatchWriter<'b> {
    fn drop(&mut self) {
        self.entry_feature_writer.complete_cuda(self.count);
    }
}

pub struct EntryFeatureWriter<'b> {
    batch: &'b mut Batch,
    index_in_batch: usize,
}

impl<'b> EntryFeatureWriter<'b> {
    pub fn sparse(self) -> SparseBatchWriter<'b> {
        SparseBatchWriter {
            entry_feature_writer: self,
        }
    }

    pub fn cuda(self) -> CudaBatchWriter<'b> {
        CudaBatchWriter {
            entry_feature_writer: self,
            count: 0,
        }
    }

    fn add_feature_sparse(&mut self, stm_feature: i64, nstm_feature: i64) {
        let index = self.batch.total_features * 2;
        self.batch.stm_feature_buffer[index] = self.index_in_batch as i64;
        self.batch.nstm_feature_buffer[index] = self.index_in_batch as i64;
        self.batch.stm_feature_buffer[index + 1] = stm_feature;
        self.batch.nstm_feature_buffer[index + 1] = nstm_feature;
        self.batch.total_features += 1;
    }

    fn add_feature_cuda(&mut self, stm_feature: i64, nstm_feature: i64) {
        self.batch.stm_feature_buffer[self.batch.total_features] = stm_feature;
        self.batch.nstm_feature_buffer[self.batch.total_features + 1] = nstm_feature;
        self.batch.total_features += 1;
    }

    fn complete_cuda(&mut self, count: usize) {
        let left_to_fill = self.batch.max_features - count;
        for _ in 0..left_to_fill {
            self.batch.stm_feature_buffer[self.batch.total_features] = -1;
            self.batch.nstm_feature_buffer[self.batch.total_features] = -1;
            self.batch.total_features += 1;
        }
    }
}
